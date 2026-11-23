namespace identity.Observability;

public static class InstanceInfo
{
    public static string Id { get; } =
        Environment.GetEnvironmentVariable("INSTANCE_ID") is { Length: > 0 } instanceId
            ? instanceId
            : Environment.MachineName;
}
