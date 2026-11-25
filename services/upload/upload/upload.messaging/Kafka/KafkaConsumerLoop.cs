using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Upload.Messaging.Kafka;

/// <summary>
/// Consumidor Kafka resiliente, base dos consumidores do Upload e do media-processor.
/// <list type="bullet">
/// <item>Consumer group com commit manual depois do efeito (R1/R4).</item>
/// <item>Reconexão automática: erro de Kafka nunca derruba o serviço (R6).</item>
/// <item>Mensagem inválida: N tentativas e depois &lt;tópico&gt;.dlq, e as outras seguem (F4).</item>
/// <item>Dependência fora do ar (MySQL, S3): espera e tenta de novo, sem DLQ.</item>
/// <item>Job demorado (FFmpeg de horas): a partição é pausada e o consumidor continua chamando
/// Consume, então o processo segue vivo no grupo. Se a partição for revogada, o job é cancelado
/// e o offset não é confirmado, e outra réplica assume (F2/F8).</item>
/// </list>
/// </summary>
public abstract class KafkaConsumerLoop : BackgroundService
{
    private static readonly TimeSpan QuickJobThreshold = TimeSpan.FromSeconds(1);

    private readonly KafkaSettings _kafka;
    private readonly IProducer<string, string> _producer;

    protected KafkaConsumerLoop(KafkaSettings kafka, IProducer<string, string> producer, ILogger logger)
    {
        _kafka = kafka;
        _producer = producer;
        Logger = logger;
    }

    protected ILogger Logger { get; }

    /// <summary>Tópico → tipo de evento esperado nele.</summary>
    protected abstract IReadOnlyDictionary<string, string> Subscriptions { get; }

    /// <summary>Quantos consumidores independentes esta instância roda (escala vertical).</summary>
    protected virtual int Workers => 1;

    protected abstract Task ProcessAsync(MessageEnvelope message, string topic, CancellationToken cancellationToken);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Enumerable.Range(1, Math.Max(1, Workers))
            .Select(worker => Task.Run(() => RunWorkerAsync(worker, stoppingToken), CancellationToken.None));
        return Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(int worker, CancellationToken stoppingToken)
    {
        var job = new JobState();
        var config = new ConsumerConfig
        {
            BootstrapServers = _kafka.BootstrapServers,
            GroupId = _kafka.ConsumerGroup,
            ClientId = $"{_kafka.ConsumerGroup}-{InstanceInfo.Id}-{worker}",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            AllowAutoCreateTopics = false,
            SessionTimeoutMs = 10000,
            HeartbeatIntervalMs = 3000,
            MaxPollIntervalMs = 300000,
            ReconnectBackoffMs = 1000,
            ReconnectBackoffMaxMs = 10000,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        };

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, error) => Logger.LogWarning(
                "Erro no consumidor Kafka: {Reason} instancia={InstanceId}", error.Reason, InstanceInfo.Id))
            .SetPartitionsAssignedHandler((_, partitions) => Logger.LogInformation(
                "Partições atribuídas {Partitions} grupo={Group} instancia={InstanceId}",
                Describe(partitions), _kafka.ConsumerGroup, InstanceInfo.Id))
            .SetPartitionsRevokedHandler((_, partitions) => job.OnLost(partitions.Select(p => p.TopicPartition), Logger, "revogada"))
            .SetPartitionsLostHandler((_, partitions) => job.OnLost(partitions.Select(p => p.TopicPartition), Logger, "perdida"))
            .Build();

        consumer.Subscribe(Subscriptions.Keys);
        Logger.LogInformation(
            "Consumindo {Topics} no grupo {Group} worker={Worker} instancia={InstanceId}",
            string.Join(", ", Subscriptions.Keys), _kafka.ConsumerGroup, worker, InstanceInfo.Id);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException ex)
                {
                    Logger.LogWarning("Falha ao consumir do Kafka: {Reason} instancia={InstanceId}", ex.Error.Reason, InstanceInfo.Id);
                    await DelayAsync(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }
                catch (KafkaException ex)
                {
                    Logger.LogWarning("Erro de Kafka: {Reason} instancia={InstanceId}", ex.Error.Reason, InstanceInfo.Id);
                    await DelayAsync(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                if (result is null || result.IsPartitionEOF)
                {
                    continue;
                }

                await HandleKeepingAliveAsync(consumer, result, job, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                consumer.Close();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Falha ao fechar o consumidor Kafka.");
            }
        }
    }

    private async Task HandleKeepingAliveAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> result,
        JobState job,
        CancellationToken stoppingToken)
    {
        using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        job.Begin(result.TopicPartition, jobCancellation);
        var handling = HandleAsync(result, jobCancellation.Token);
        List<TopicPartition>? paused = null;

        try
        {
            if (await Task.WhenAny(handling, Task.Delay(QuickJobThreshold, CancellationToken.None)) != handling)
            {
                // Job demorado: pausa tudo e continua chamando Consume para manter o consumidor no grupo.
                paused = consumer.Assignment.ToList();
                consumer.Pause(paused);

                while (!handling.IsCompleted)
                {
                    ConsumeResult<string, string>? extra = null;
                    try
                    {
                        extra = consumer.Consume(TimeSpan.FromMilliseconds(200));
                    }
                    catch (KafkaException ex)
                    {
                        Logger.LogWarning("Erro de Kafka durante um job longo: {Reason} instancia={InstanceId}", ex.Error.Reason, InstanceInfo.Id);
                    }

                    if (extra is not null && !extra.IsPartitionEOF)
                    {
                        // Veio de uma partição atribuída agora: devolve a mensagem e pausa até o job acabar.
                        consumer.Seek(extra.TopicPartitionOffset);
                        consumer.Pause([extra.TopicPartition]);
                        paused.Add(extra.TopicPartition);
                    }
                }
            }

            var commit = await handling;
            if (job.Lost)
            {
                Logger.LogWarning(
                    "Partição {Partition} saiu desta réplica durante o processamento; o offset não será confirmado e outra réplica assume. key={Key} instancia={InstanceId}",
                    result.TopicPartition, result.Message.Key, InstanceInfo.Id);
            }
            else if (commit)
            {
                TryCommit(consumer, result);
            }
        }
        finally
        {
            job.End();
            if (paused is not null)
            {
                try
                {
                    consumer.Resume(consumer.Assignment);
                }
                catch (KafkaException ex)
                {
                    Logger.LogWarning("Falha ao retomar as partições: {Reason}", ex.Error.Reason);
                }
            }
        }
    }

    /// <returns>true quando o offset pode ser confirmado.</returns>
    private async Task<bool> HandleAsync(ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        var invalidAttempts = 0;
        var transientAttempts = 0;

        while (true)
        {
            try
            {
                var envelope = MessageEnvelope.Parse(result.Message.Value);
                if (Subscriptions.TryGetValue(result.Topic, out var expected) && !string.Equals(envelope.Type, expected, StringComparison.Ordinal))
                {
                    throw new InvalidMessageException($"Tipo {envelope.Type} não corresponde ao tópico {result.Topic} (esperado {expected}).");
                }

                await ProcessAsync(envelope, result.Topic, cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex) when (ex is not InvalidMessageException && TransientErrors.IsTransient(ex))
            {
                transientAttempts++;
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(transientAttempts, 5) - 1)));
                Logger.LogWarning(
                    "Dependência indisponível; nova tentativa em {Delay}s (tentativa {Attempt}). topico={Topic} key={Key} erro={Error} instancia={InstanceId}",
                    delay.TotalSeconds, transientAttempts, result.Topic, result.Message.Key, ex.GetBaseException().Message, InstanceInfo.Id);
                if (!await DelayAsync(delay, cancellationToken))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                invalidAttempts++;
                Logger.LogWarning(
                    "Falha ao processar mensagem (tentativa {Attempt}/{MaxAttempts}). topico={Topic} key={Key} erro={Error} instancia={InstanceId}",
                    invalidAttempts, _kafka.MaxAttempts, result.Topic, result.Message.Key, ex.Message, InstanceInfo.Id);

                if (invalidAttempts >= _kafka.MaxAttempts)
                {
                    return await PublishToDlqAsync(result, ex, cancellationToken);
                }

                if (!await DelayAsync(TimeSpan.FromMilliseconds(200 * invalidAttempts), cancellationToken))
                {
                    return false;
                }
            }
        }
    }

    private void TryCommit(IConsumer<string, string> consumer, ConsumeResult<string, string> result)
    {
        try
        {
            consumer.Commit(result);
        }
        catch (KafkaException ex)
        {
            Logger.LogWarning(
                "Falha ao confirmar o offset; a mensagem pode ser reentregue (a idempotência cobre). topico={Topic} erro={Error} instancia={InstanceId}",
                result.Topic, ex.Error.Reason, InstanceInfo.Id);
        }
    }

    private async Task<bool> PublishToDlqAsync(ConsumeResult<string, string> result, Exception error, CancellationToken cancellationToken)
    {
        var dlqTopic = $"{result.Topic}.dlq";
        var message = new Message<string, string>
        {
            Key = result.Message.Key,
            Value = result.Message.Value,
            Headers = new Headers
            {
                new Header("x-original-topic", Encoding.UTF8.GetBytes(result.Topic)),
                new Header("x-error", Encoding.UTF8.GetBytes(error.Message)),
                new Header("x-attempts", Encoding.UTF8.GetBytes(_kafka.MaxAttempts.ToString())),
                new Header("x-instance", Encoding.UTF8.GetBytes(InstanceInfo.Id)),
                new Header("x-failed-at", Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O")))
            }
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _producer.ProduceAsync(dlqTopic, message, cancellationToken);
                Logger.LogError(
                    "Mensagem enviada para DLQ topico={DlqTopic} key={Key} tentativas={Attempts} erro={Error} instancia={InstanceId}",
                    dlqTopic, message.Key, _kafka.MaxAttempts, error.Message, InstanceInfo.Id);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Falha ao publicar na DLQ {DlqTopic}; nova tentativa em 2s. instancia={InstanceId}", dlqTopic, InstanceInfo.Id);
                await DelayAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        return false;
    }

    private static string Describe(IEnumerable<TopicPartition> partitions) =>
        string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition.Value}]"));

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Qual partição está em processamento, para cancelar o job se ela sair desta réplica.</summary>
    private sealed class JobState
    {
        private readonly Lock _gate = new();
        private TopicPartition? _partition;
        private CancellationTokenSource? _cancellation;

        public bool Lost { get; private set; }

        public void Begin(TopicPartition partition, CancellationTokenSource cancellation)
        {
            lock (_gate)
            {
                _partition = partition;
                _cancellation = cancellation;
                Lost = false;
            }
        }

        public void End()
        {
            lock (_gate)
            {
                _partition = null;
                _cancellation = null;
            }
        }

        public void OnLost(IEnumerable<TopicPartition> partitions, ILogger logger, string how)
        {
            lock (_gate)
            {
                var list = partitions.ToList();
                logger.LogInformation("Partições {How}s {Partitions} instancia={InstanceId}", how, Describe(list), InstanceInfo.Id);

                if (_partition is null || !list.Contains(_partition))
                {
                    return;
                }

                Lost = true;
                _cancellation?.Cancel();
            }
        }
    }
}
