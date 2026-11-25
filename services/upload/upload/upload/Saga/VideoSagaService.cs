using Microsoft.EntityFrameworkCore;
using Upload.Data;
using Upload.Messaging;
using Upload.Messaging.Kafka;
using Upload.Messaging.Outbox;
using Upload.Messaging.Persistence;

namespace Upload.Saga;

/// <summary>
/// Executa a saga sobre o banco: relê o vídeo, decide com o VideoSaga e grava a transição, o
/// processed_messages (R1), o outbox, as limpezas, a notificação e o histórico num único
/// SaveChanges. Se duas réplicas mexerem no mesmo vídeo, o concurrency token faz a segunda
/// reler e decidir de novo.
/// </summary>
public sealed class VideoSagaService(
    IDbContextFactory<UploadDbContext> factory,
    VideoSaga saga,
    KafkaTopics topics,
    ILogger<VideoSagaService> logger)
{
    public async Task CreateAsync(VideoUpload video, CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        context.Videos.Add(video);
        var decision = saga.Create(video);
        Apply(context, video, decision, messageId: null, video.CreatedAt);
        await context.SaveChangesAsync(cancellationToken);
        LogDecision("UploadCreated", video, decision, messageId: null, video.VideoId);
    }

    /// <summary>Evento vindo do Kafka: idempotente pelo messageId.</summary>
    public Task HandleMessageAsync(MessageEnvelope message, string videoId, SagaEvent sagaEvent, CancellationToken cancellationToken) =>
        OptimisticRetry.RunAsync(factory, async context =>
        {
            if (await Idempotency.AlreadyProcessedAsync(context, message.MessageId, cancellationToken))
            {
                LogDuplicate(message, videoId);
                return;
            }

            var now = DateTime.UtcNow;
            Idempotency.Register(context, message, now);

            var video = await context.Videos.SingleOrDefaultAsync(v => v.VideoId == videoId, cancellationToken);
            if (video is null)
            {
                logger.LogWarning(
                    "Evento {Type} para vídeo desconhecido ignorado videoId={VideoId} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                    message.Type, videoId, message.MessageId, message.CorrelationId, InstanceInfo.Id);
                await SaveIgnoringDuplicateAsync(context, message, videoId, cancellationToken);
                return;
            }

            if (sagaEvent is TimeoutCheckEvent)
            {
                throw new InvalidOperationException("TimeoutCheck não vem do Kafka.");
            }

            var decision = saga.Handle(video, sagaEvent, now);
            Apply(context, video, decision, message.MessageId, now);

            if (await SaveIgnoringDuplicateAsync(context, message, videoId, cancellationToken))
            {
                LogDecision(message.Type, video, decision, message.MessageId, message.CorrelationId);
            }
        }, logger, cancellationToken);

    /// <summary>Comando da API ou do varredor de prazos. Devolve null se o vídeo não existir (ou não for do dono).</summary>
    public Task<CommandResult?> HandleCommandAsync(string videoId, string? ownerId, SagaEvent sagaEvent, CancellationToken cancellationToken) =>
        OptimisticRetry.RunAsync(factory, async context =>
        {
            var video = await context.Videos.SingleOrDefaultAsync(
                v => v.VideoId == videoId && (ownerId == null || v.OwnerId == ownerId), cancellationToken);
            if (video is null)
            {
                return null;
            }

            if (sagaEvent is TimeoutCheckEvent)
            {
                var lastPartAt = await context.Parts
                    .Where(part => part.VideoId == videoId)
                    .MaxAsync(part => (DateTime?)part.UploadedAt, cancellationToken);
                sagaEvent = new TimeoutCheckEvent(lastPartAt);
            }

            var now = DateTime.UtcNow;
            var decision = saga.Handle(video, sagaEvent, now);
            if (decision.Applied)
            {
                Apply(context, video, decision, messageId: null, now);
                await context.SaveChangesAsync(cancellationToken);
                LogDecision(sagaEvent.Name, video, decision, messageId: null, video.VideoId);
            }

            return new CommandResult(video, decision);
        }, logger, cancellationToken);

    private void Apply(UploadDbContext context, VideoUpload video, SagaDecision decision, string? messageId, DateTime now)
    {
        if (!decision.Applied)
        {
            return;
        }

        // Toda decisão aplicada toca a linha do vídeo: o concurrency token protege a transição inteira.
        video.UpdatedAt = now;

        foreach (var action in decision.Actions)
        {
            switch (action)
            {
                case PublishEvent publish:
                    OutboxWriter.Enqueue(context, TopicName(publish.Topic), video.VideoId, publish.Type, video.VideoId, publish.Payload, now);
                    break;
                case ScheduleCleanup cleanup:
                    context.StorageCleanups.Add(new StorageCleanup
                    {
                        VideoId = video.VideoId,
                        Action = cleanup.Action,
                        Bucket = cleanup.Bucket,
                        KeyOrPrefix = cleanup.KeyOrPrefix,
                        UploadId = cleanup.UploadId,
                        Reason = Truncate(video.FailureReason ?? video.Status.ToString(), 60)!,
                        NotBefore = now + cleanup.Delay,
                        CreatedAt = now
                    });
                    break;
                case NotifyOwner notify:
                    context.Notifications.Add(new UserNotification
                    {
                        UserId = video.OwnerId,
                        VideoId = video.VideoId,
                        Type = notify.Type,
                        Message = Truncate(notify.Message, 500)!,
                        CreatedAt = now
                    });
                    logger.LogInformation(
                        "Notificação ao usuário ownerId={OwnerId} videoId={VideoId} tipo={Type}: {Message} instancia={InstanceId}",
                        video.OwnerId, video.VideoId, notify.Type, notify.Message, InstanceInfo.Id);
                    break;
                case RecordHistory history:
                    context.History.Add(new VideoHistory
                    {
                        VideoId = video.VideoId,
                        FromStatus = history.From?.ToString(),
                        ToStatus = history.To.ToString(),
                        Reason = Truncate(history.Reason, 60)!,
                        Detail = Truncate(history.Detail, 500),
                        MessageId = messageId,
                        InstanceId = InstanceInfo.Id,
                        At = now
                    });
                    break;
            }
        }
    }

    private async Task<bool> SaveIgnoringDuplicateAsync(UploadDbContext context, MessageEnvelope message, string videoId, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && DbErrors.IsDuplicateKey(ex))
        {
            // Outra réplica gravou o mesmo messageId ao mesmo tempo: a transação inteira é descartada.
            LogDuplicate(message, videoId);
            return false;
        }
    }

    private string TopicName(SagaTopic topic) => topic switch
    {
        SagaTopic.QuotaReserveRequested => topics.QuotaReserveRequested,
        SagaTopic.QuotaReleaseRequested => topics.QuotaReleaseRequested,
        SagaTopic.VideoUploaded => topics.VideoUploaded,
        SagaTopic.VideoPublished => topics.VideoPublished,
        SagaTopic.VideoRemoved => topics.VideoRemoved,
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, null)
    };

    private void LogDuplicate(MessageEnvelope message, string videoId) =>
        logger.LogInformation(
            "Mensagem duplicada ignorada tipo={Type} videoId={VideoId} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
            message.Type, videoId, message.MessageId, message.CorrelationId, InstanceInfo.Id);

    private void LogDecision(string eventName, VideoUpload video, SagaDecision decision, string? messageId, string correlationId) =>
        logger.LogInformation(
            "Saga {Event} {Outcome}: {Description} videoId={VideoId} status={Status} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
            eventName, decision.Applied ? "aplicado" : "ignorado", decision.Description, video.VideoId, video.Status,
            messageId ?? "-", correlationId, InstanceInfo.Id);

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}

public sealed record CommandResult(VideoUpload Video, SagaDecision Decision);
