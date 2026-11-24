using Upload.Domain;
using Upload.Messaging.Contracts;

namespace Upload.Saga;

/// <summary>Eventos que movem a saga (do Kafka, da API ou do varredor de prazos).</summary>
public abstract record SagaEvent(string Name);

public sealed record QuotaReservedEvent() : SagaEvent("QuotaReserved");

public sealed record QuotaRejectedEvent(string Reason) : SagaEvent("QuotaRejected");

/// <summary>O cliente avisou que mandou todas as partes (POST complete).</summary>
public sealed record CompleteRequestedEvent() : SagaEvent("CompleteRequested");

/// <summary>O original foi montado no S3 (CompleteMultipartUpload concluído).</summary>
public sealed record UploadCompletedEvent() : SagaEvent("UploadCompleted");

public sealed record ProcessingStartedEvent(int Attempt, string InstanceId) : SagaEvent("VideoProcessingStarted");

public sealed record ProcessedEvent(VideoProcessedPayload Result) : SagaEvent("VideoProcessed");

public sealed record ProcessingFailedEvent(string Reason, string? Detail) : SagaEvent("VideoProcessingFailed");

/// <param name="LastPartAt">Última parte recebida: o upload só é abandonado se ficar parado.</param>
public sealed record TimeoutCheckEvent(DateTime? LastPartAt) : SagaEvent("TimeoutCheck");

public sealed record DeleteRequestedEvent() : SagaEvent("DeleteRequested");

/// <summary>Tópicos que a saga publica (nomes reais vêm da configuração).</summary>
public enum SagaTopic
{
    QuotaReserveRequested,
    QuotaReleaseRequested,
    VideoUploaded,
    VideoPublished,
    VideoRemoved
}

/// <summary>Efeitos decididos pela saga e gravados na mesma transação da transição.</summary>
public abstract record SagaAction;

public sealed record PublishEvent(SagaTopic Topic, string Type, object Payload) : SagaAction;

public sealed record ScheduleCleanup(CleanupAction Action, string Bucket, string KeyOrPrefix, string? UploadId, TimeSpan Delay) : SagaAction;

public sealed record NotifyOwner(string Type, string Message) : SagaAction;

public sealed record RecordHistory(VideoStatus? From, VideoStatus To, string Reason, string? Detail) : SagaAction;

/// <summary>Resultado de uma decisão: aplicada (com efeitos) ou ignorada (idempotente).</summary>
public sealed record SagaDecision(bool Applied, string Description, IReadOnlyList<SagaAction> Actions)
{
    public static SagaDecision Apply(string description, IReadOnlyList<SagaAction> actions) => new(true, description, actions);

    public static SagaDecision Ignore(string description) => new(false, description, []);
}

/// <summary>Motivos de falha gravados em video_upload.failure_reason.</summary>
public static class FailureReasons
{
    public const string ProcessingFailed = "PROCESSING_FAILED";
    public const string DeadlineExpired = "DEADLINE_EXPIRED";
    public const string QueueTimeout = "QUEUE_TIMEOUT";
    public const string UploadAbandoned = "UPLOAD_ABANDONED";
    public const string QuotaTimeout = "QUOTA_TIMEOUT";
    public const string CancelledByUser = "CANCELLED_BY_USER";
}
