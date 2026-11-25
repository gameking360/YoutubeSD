namespace Upload.Messaging;

/// <summary>Identifica a instância (réplica) nos logs, nos headers e nos leases.</summary>
public static class InstanceInfo
{
    public static string Id { get; } =
        Environment.GetEnvironmentVariable("INSTANCE_ID") is { Length: > 0 } instanceId
            ? instanceId
            : Environment.MachineName;
}
