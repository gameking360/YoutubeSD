using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace Upload.Messaging.Kafka;

public static class KafkaProducerFactory
{
    public static IServiceCollection AddKafkaProducer(this IServiceCollection services, KafkaSettings kafka, string service)
    {
        services.AddSingleton<IProducer<string, string>>(_ =>
        {
            var config = new ProducerConfig
            {
                BootstrapServers = kafka.BootstrapServers,
                ClientId = $"{service}-{InstanceInfo.Id}-producer",
                Acks = Acks.All,
                EnableIdempotence = true,
                MessageTimeoutMs = 5000,
                AllowAutoCreateTopics = false,
                ReconnectBackoffMs = 1000,
                ReconnectBackoffMaxMs = 10000
            };

            return new ProducerBuilder<string, string>(config).Build();
        });

        return services;
    }
}
