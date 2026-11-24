namespace Upload.Messaging.Contracts;

/// <summary>Tipos de evento (campo "type" do envelope). Os esquemas ficam em contracts/.</summary>
public static class EventTypes
{
    public const string QuotaReserveRequested = "QuotaReserveRequested";
    public const string QuotaReserved = "QuotaReserved";
    public const string QuotaRejected = "QuotaRejected";
    public const string QuotaReleaseRequested = "QuotaReleaseRequested";
    public const string VideoUploaded = "VideoUploaded";
    public const string VideoProcessingStarted = "VideoProcessingStarted";
    public const string VideoProcessed = "VideoProcessed";
    public const string VideoProcessingFailed = "VideoProcessingFailed";
    public const string VideoPublished = "VideoPublished";
    public const string VideoRemoved = "VideoRemoved";
}

/// <summary>Resolução HLS. Mesmo formato que o Download guarda no catálogo.</summary>
public sealed record Rendition(string Name, int Width, int Height, int Bandwidth);

/// <summary>contracts/quota-reserve-requested.v1.schema.json</summary>
public sealed record QuotaReserveRequestedPayload(string VideoId, string OwnerId, string Title);

/// <summary>contracts/quota-release-requested.v1.schema.json</summary>
public sealed record QuotaReleaseRequestedPayload(string VideoId, string OwnerId, string Reason);

/// <summary>contracts/video-uploaded.v1.schema.json</summary>
public sealed record VideoUploadedPayload(
    string VideoId,
    string OwnerId,
    string Title,
    string RawBucket,
    string RawKey,
    long SizeBytes,
    string? ContentType,
    string HlsBucket,
    string HlsPrefix,
    DateTime QueueDeadline,
    DateTime UploadedAt);

/// <summary>contracts/video-processing-started.v1.schema.json</summary>
public sealed record VideoProcessingStartedPayload(string VideoId, int Attempt, string InstanceId, DateTime StartedAt);

/// <summary>contracts/video-processed.v1.schema.json</summary>
public sealed record VideoProcessedPayload(
    string VideoId,
    string HlsBucket,
    string MasterKey,
    int DurationSec,
    IReadOnlyList<Rendition> Renditions,
    string? ThumbnailKey,
    DateTime ProcessedAt,
    string InstanceId);

/// <summary>contracts/video-processing-failed.v1.schema.json</summary>
public sealed record VideoProcessingFailedPayload(
    string VideoId,
    string Reason,
    string? Detail,
    int Attempts,
    DateTime FailedAt,
    string InstanceId);

/// <summary>contracts/video-published.v1.schema.json (consumido pelo Download e pelo Identity).</summary>
public sealed record VideoPublishedPayload(
    string VideoId,
    string OwnerId,
    string Title,
    string Visibility,
    int DurationSec,
    string HlsBucket,
    string MasterKey,
    IReadOnlyList<Rendition> Renditions,
    string? ThumbnailKey,
    DateTime PublishedAt);

/// <summary>contracts/video-removed.v1.schema.json</summary>
public sealed record VideoRemovedPayload(string VideoId, string Reason, DateTime RemovedAt);

/// <summary>Motivos de falha do processamento (enum do contrato video-processing-failed).</summary>
public static class ProcessingFailureReasons
{
    public const string SimulatedFailure = "SIMULATED_FAILURE";
    public const string FfmpegError = "FFMPEG_ERROR";
    public const string InvalidMedia = "INVALID_MEDIA";
    public const string SourceNotFound = "SOURCE_NOT_FOUND";
    public const string QueueTimeout = "QUEUE_TIMEOUT";
    public const string TooManyAttempts = "TOO_MANY_ATTEMPTS";
    public const string Timeout = "TIMEOUT";
    public const string StorageError = "STORAGE_ERROR";
    public const string InternalError = "INTERNAL_ERROR";
}
