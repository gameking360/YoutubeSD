using Microsoft.Extensions.Configuration;

namespace identity.Configuration;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroup { get; set; } = "identity";
    public int MaxAttempts { get; set; } = 3;
    public KafkaTopics Topics { get; set; } = new();

    public static KafkaOptions Resolve(IConfiguration configuration)
    {
        var options = configuration.GetSection("Kafka").Get<KafkaOptions>() ?? new KafkaOptions();

        var connectionString = configuration.GetConnectionString("kafka");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            options.BootstrapServers = Normalize(connectionString);
        }

        var fallback = configuration["KAFKA_BOOTSTRAP_SERVERS"];
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            options.BootstrapServers = Normalize(fallback);
        }

        options.MaxAttempts = Math.Max(1, options.MaxAttempts);
        return options;
    }

    private static string Normalize(string value) =>
        string.Join(",", value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(server => server.Contains("://", StringComparison.Ordinal)
                ? server[(server.IndexOf("://", StringComparison.Ordinal) + 3)..]
                : server));
}

public sealed class KafkaTopics
{
    public string ReserveRequested { get; set; } = "quota.reserve-requested";
    public string Reserved { get; set; } = "quota.reserved";
    public string Rejected { get; set; } = "quota.rejected";
    public string ReleaseRequested { get; set; } = "quota.release-requested";
    public string VideoPublished { get; set; } = "video.published";
}
