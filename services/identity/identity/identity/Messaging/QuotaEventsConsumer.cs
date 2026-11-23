using System.Text;
using Confluent.Kafka;
using identity.Configuration;
using identity.Observability;
using identity.Services;

namespace identity.Messaging;

public sealed class QuotaEventsConsumer(
    KafkaOptions kafka,
    IProducer<string, string> producer,
    IServiceScopeFactory scopeFactory,
    ILogger<QuotaEventsConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = kafka.ConsumerGroup,
            ClientId = $"identity-{InstanceInfo.Id}",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            AllowAutoCreateTopics = false,
            SessionTimeoutMs = 10000,
            HeartbeatIntervalMs = 3000,
            MaxPollIntervalMs = 300000,
            ReconnectBackoffMs = 1000,
            ReconnectBackoffMaxMs = 10000
        };

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, error) => logger.LogWarning(
                "Erro no consumidor Kafka: {Reason} instancia={InstanceId}", error.Reason, InstanceInfo.Id))
            .Build();

        var topics = new[]
        {
            kafka.Topics.ReserveRequested,
            kafka.Topics.ReleaseRequested,
            kafka.Topics.VideoPublished
        };

        consumer.Subscribe(topics);
        logger.LogInformation(
            "Identity consumindo {Topics} no grupo {Group} instancia={InstanceId}",
            string.Join(", ", topics), kafka.ConsumerGroup, InstanceInfo.Id);

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
                    logger.LogWarning(
                        "Falha ao consumir do Kafka: {Reason} instancia={InstanceId}",
                        ex.Error.Reason, InstanceInfo.Id);
                    await DelayAsync(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }
                catch (KafkaException ex)
                {
                    logger.LogWarning(
                        "Erro de Kafka: {Reason} instancia={InstanceId}",
                        ex.Error.Reason, InstanceInfo.Id);
                    await DelayAsync(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                if (result is null || result.IsPartitionEOF)
                {
                    continue;
                }

                try
                {
                    await HandleAsync(consumer, result, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Erro inesperado ao processar mensagem; seguindo. topico={Topic} instancia={InstanceId}",
                        result.Topic, InstanceInfo.Id);
                }
            }
        }
        finally
        {
            try
            {
                consumer.Close();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao fechar o consumidor Kafka.");
            }
        }
    }

    private async Task HandleAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> result,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= kafka.MaxAttempts; attempt++)
        {
            try
            {
                var envelope = MessageEnvelope.Parse(result.Message.Value);
                await ProcessAsync(envelope, result.Topic, cancellationToken);
                TryCommit(consumer, result);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogWarning(
                    "Falha ao processar mensagem (tentativa {Attempt}/{MaxAttempts}). topico={Topic} key={Key} erro={Error} instancia={InstanceId}",
                    attempt, kafka.MaxAttempts, result.Topic, result.Message.Key, ex.Message, InstanceInfo.Id);
            }

            if (attempt < kafka.MaxAttempts)
            {
                await DelayAsync(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
        }

        await PublishToDlqAsync(result, lastError!, cancellationToken);
        TryCommit(consumer, result);
    }

    private void TryCommit(IConsumer<string, string> consumer, ConsumeResult<string, string> result)
    {
        try
        {
            consumer.Commit(result);
        }
        catch (KafkaException ex)
        {
            logger.LogWarning(
                ex,
                "Falha ao confirmar o offset; a mensagem pode ser reentregue. topico={Topic} instancia={InstanceId}",
                result.Topic, InstanceInfo.Id);
        }
    }

    private async Task ProcessAsync(MessageEnvelope message, string topic, CancellationToken cancellationToken)
    {
        var expectedType = topic switch
        {
            var value when value == kafka.Topics.ReserveRequested => "QuotaReserveRequested",
            var value when value == kafka.Topics.ReleaseRequested => "QuotaReleaseRequested",
            var value when value == kafka.Topics.VideoPublished => "VideoPublished",
            _ => throw new InvalidMessageException($"Tópico não suportado: {topic}")
        };

        if (!string.Equals(message.Type, expectedType, StringComparison.Ordinal))
        {
            throw new InvalidMessageException(
                $"Tipo {message.Type} não corresponde ao tópico {topic} (esperado {expectedType}).");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var quotas = scope.ServiceProvider.GetRequiredService<QuotaService>();

        if (topic == kafka.Topics.ReserveRequested)
        {
            await quotas.ReserveAsync(message, cancellationToken);
        }
        else if (topic == kafka.Topics.ReleaseRequested)
        {
            await quotas.ReleaseAsync(message, cancellationToken);
        }
        else
        {
            await quotas.ConfirmAsync(message, cancellationToken);
        }
    }

    private async Task PublishToDlqAsync(
        ConsumeResult<string, string> result,
        Exception error,
        CancellationToken cancellationToken)
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
                new Header("x-attempts", Encoding.UTF8.GetBytes(kafka.MaxAttempts.ToString())),
                new Header("x-instance", Encoding.UTF8.GetBytes(InstanceInfo.Id)),
                new Header("x-failed-at", Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O")))
            }
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await producer.ProduceAsync(dlqTopic, message, cancellationToken);
                logger.LogError(
                    error,
                    "Mensagem enviada para DLQ topico={DlqTopic} key={Key} tentativas={Attempts} instancia={InstanceId}",
                    dlqTopic, message.Key, kafka.MaxAttempts, InstanceInfo.Id);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Falha ao publicar na DLQ {DlqTopic}; nova tentativa em 2s. instancia={InstanceId}",
                    dlqTopic, InstanceInfo.Id);
                await DelayAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
