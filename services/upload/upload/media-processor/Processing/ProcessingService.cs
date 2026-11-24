using MediaProcessor.Configuration;
using MediaProcessor.Data;
using Microsoft.EntityFrameworkCore;
using Upload.Messaging;
using Upload.Messaging.Contracts;
using Upload.Messaging.Kafka;
using Upload.Messaging.Outbox;
using Upload.Messaging.Persistence;

namespace MediaProcessor.Processing;

/// <summary>
/// Processa um video.uploaded de ponta a ponta, com idempotência e tentativas que sobrevivem a quedas:
/// <list type="number">
/// <item>messageId já em processed_messages → duplicata ignorada (F3).</item>
/// <item>Prazo de fila vencido → video.processing-failed(QUEUE_TIMEOUT), sem processar.</item>
/// <item>Cada tentativa: attempts++ e video.processing-started no outbox (o Upload arma o prazo).</item>
/// <item>Sucesso: processed_messages + video.processed no mesmo SaveChanges. Se outra réplica já
/// concluiu, a chave duplicada descarta este resultado (F2).</item>
/// <item>Tentativas esgotadas ou erro definitivo: video.processing-failed (o Upload compensa, F1).</item>
/// </list>
/// </summary>
public sealed class ProcessingService(
    IDbContextFactory<MediaDbContext> factory,
    MediaPipeline pipeline,
    ProcessorOptions options,
    KafkaTopics topics,
    ILogger<ProcessingService> logger)
{
    public async Task ProcessAsync(MessageEnvelope message, VideoUploadedPayload job, CancellationToken cancellationToken)
    {
        await using (var context = await factory.CreateDbContextAsync(cancellationToken))
        {
            if (await Idempotency.AlreadyProcessedAsync(context, message.MessageId, cancellationToken))
            {
                logger.LogInformation(
                    "Mensagem duplicada ignorada tipo={Type} videoId={VideoId} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                    message.Type, job.VideoId, message.MessageId, message.CorrelationId, InstanceInfo.Id);
                return;
            }
        }

        if (job.QueueDeadline < DateTime.UtcNow)
        {
            await FinishFailedAsync(message, job, ProcessingFailureReasons.QueueTimeout,
                $"o vídeo esperou na fila além de {job.QueueDeadline:O}", attempts: 0, cancellationToken);
            return;
        }

        while (true)
        {
            var attempt = await BeginAttemptAsync(message, job, cancellationToken);
            if (attempt > options.MaxAttempts)
            {
                await FinishFailedAsync(message, job, ProcessingFailureReasons.TooManyAttempts,
                    $"{attempt - 1} tentativas já feitas para este vídeo", attempt - 1, cancellationToken);
                return;
            }

            PipelineResult result;
            try
            {
                result = await pipeline.RunAsync(job, attempt, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Processamento interrompido (partição revogada ou desligamento); outra réplica assume. videoId={VideoId} tentativa={Attempt} instancia={InstanceId}",
                    job.VideoId, attempt, InstanceInfo.Id);
                throw;
            }
            catch (Exception ex)
            {
                var failure = ex as ProcessingException;
                var reason = failure?.Reason ?? ProcessingFailureReasons.InternalError;
                var retryable = failure?.Retryable ?? true;

                if (!retryable || attempt >= options.MaxAttempts)
                {
                    await FinishFailedAsync(message, job, reason, ex.Message, attempt, cancellationToken);
                    return;
                }

                var backoff = TimeSpan.FromSeconds(Math.Min(30, 2 * attempt));
                logger.LogWarning(
                    "Tentativa {Attempt}/{MaxAttempts} falhou ({Reason}); nova tentativa em {Backoff}s. erro={Error} videoId={VideoId} instancia={InstanceId}",
                    attempt, options.MaxAttempts, reason, backoff.TotalSeconds, ex.Message, job.VideoId, InstanceInfo.Id);
                await RecordErrorAsync(job.VideoId, $"{reason}: {ex.Message}", cancellationToken);
                await Task.Delay(backoff, cancellationToken);
                continue;
            }

            // Fora do try: se o MySQL cair aqui, a exceção sobe e o consumidor espera ele voltar.
            await FinishProcessedAsync(message, job, result, cancellationToken);
            return;
        }
    }

    private Task<int> BeginAttemptAsync(MessageEnvelope message, VideoUploadedPayload job, CancellationToken cancellationToken) =>
        OptimisticRetry.RunAsync(factory, async context =>
        {
            var now = DateTime.UtcNow;
            var row = await context.Jobs.SingleOrDefaultAsync(j => j.VideoId == job.VideoId, cancellationToken);
            if (row is null)
            {
                row = new ProcessingJob { VideoId = job.VideoId };
                context.Jobs.Add(row);
            }

            row.Attempts++;
            row.LastMessageId = message.MessageId;
            row.InstanceId = InstanceInfo.Id;
            row.Status = JobStatus.RUNNING;
            row.StartedAt = now;
            row.FinishedAt = null;

            if (row.Attempts <= options.MaxAttempts)
            {
                OutboxWriter.Enqueue(context, topics.VideoProcessingStarted, job.VideoId, EventTypes.VideoProcessingStarted,
                    message.CorrelationId, new VideoProcessingStartedPayload(job.VideoId, row.Attempts, InstanceInfo.Id, now), now);
            }

            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Processamento iniciado videoId={VideoId} tentativa={Attempt}/{MaxAttempts} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                job.VideoId, row.Attempts, options.MaxAttempts, message.MessageId, message.CorrelationId, InstanceInfo.Id);

            return row.Attempts;
        }, logger, cancellationToken);

    private Task FinishProcessedAsync(MessageEnvelope message, VideoUploadedPayload job, PipelineResult result, CancellationToken cancellationToken) =>
        OptimisticRetry.RunAsync(factory, async context =>
        {
            var now = DateTime.UtcNow;
            Idempotency.Register(context, message, now);

            var row = await context.Jobs.SingleAsync(j => j.VideoId == job.VideoId, cancellationToken);
            row.Status = JobStatus.DONE;
            row.FinishedAt = now;
            row.LastError = null;

            OutboxWriter.Enqueue(context, topics.VideoProcessed, job.VideoId, EventTypes.VideoProcessed, message.CorrelationId,
                new VideoProcessedPayload(job.VideoId, job.HlsBucket, result.MasterKey, result.DurationSec, result.Renditions,
                    result.ThumbnailKey, now, InstanceInfo.Id), now);

            if (await SaveOnceAsync(context, message, job.VideoId, cancellationToken))
            {
                logger.LogInformation(
                    "Vídeo processado videoId={VideoId} duracao={Duration}s arquivos={Files} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                    job.VideoId, result.DurationSec, result.Files, message.MessageId, message.CorrelationId, InstanceInfo.Id);
            }
        }, logger, cancellationToken);

    private Task FinishFailedAsync(MessageEnvelope message, VideoUploadedPayload job, string reason, string detail, int attempts, CancellationToken cancellationToken) =>
        OptimisticRetry.RunAsync(factory, async context =>
        {
            var now = DateTime.UtcNow;
            Idempotency.Register(context, message, now);

            var row = await context.Jobs.SingleOrDefaultAsync(j => j.VideoId == job.VideoId, cancellationToken);
            if (row is null)
            {
                row = new ProcessingJob { VideoId = job.VideoId, LastMessageId = message.MessageId, InstanceId = InstanceInfo.Id };
                context.Jobs.Add(row);
            }

            row.Status = JobStatus.FAILED;
            row.FinishedAt = now;
            row.LastError = Truncate($"{reason}: {detail}", 500);

            OutboxWriter.Enqueue(context, topics.VideoProcessingFailed, job.VideoId, EventTypes.VideoProcessingFailed, message.CorrelationId,
                new VideoProcessingFailedPayload(job.VideoId, reason, Truncate(detail, 400), attempts, now, InstanceInfo.Id), now);

            if (await SaveOnceAsync(context, message, job.VideoId, cancellationToken))
            {
                logger.LogWarning(
                    "Processamento falhou videoId={VideoId} motivo={Reason} tentativas={Attempts} detalhe={Detail} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                    job.VideoId, reason, attempts, detail, message.MessageId, message.CorrelationId, InstanceInfo.Id);
            }
        }, logger, cancellationToken);

    private async Task RecordErrorAsync(string videoId, string error, CancellationToken cancellationToken)
    {
        try
        {
            await OptimisticRetry.RunAsync(factory, async context =>
            {
                var row = await context.Jobs.SingleOrDefaultAsync(j => j.VideoId == videoId, cancellationToken);
                if (row is not null)
                {
                    row.LastError = Truncate(error, 500);
                    await context.SaveChangesAsync(cancellationToken);
                }
            }, logger, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Não foi possível registrar o erro do job {VideoId}: {Error}", videoId, ex.GetBaseException().Message);
        }
    }

    /// <returns>false se outra réplica já tinha registrado o mesmo messageId (resultado descartado).</returns>
    private async Task<bool> SaveOnceAsync(MediaDbContext context, MessageEnvelope message, string videoId, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && DbErrors.IsDuplicateKey(ex))
        {
            logger.LogInformation(
                "Resultado descartado: outra réplica já concluiu este vídeo. videoId={VideoId} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                videoId, message.MessageId, message.CorrelationId, InstanceInfo.Id);
            return false;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
