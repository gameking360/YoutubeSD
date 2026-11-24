using Upload.Domain;
using Upload.Messaging.Contracts;
using Upload.Messaging.Persistence;

namespace Upload.Data;

/// <summary>
/// Estado da saga de um vídeo (tabela video_upload). Os campos de catálogo usam os mesmos
/// nomes e tipos do video_catalog do Download.
/// </summary>
public sealed class VideoUpload : IVersioned
{
    public string VideoId { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string Title { get; set; } = "";
    public Visibility Visibility { get; set; } = Visibility.PUBLIC;
    public VideoStatus Status { get; set; } = VideoStatus.PENDING;
    public int? DurationSec { get; set; }
    public string? HlsBucket { get; set; }
    public string? MasterKey { get; set; }
    public List<Rendition>? Renditions { get; set; }
    public string? ThumbnailKey { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? RemovedAt { get; set; }

    public string FileName { get; set; } = "";
    public string Extension { get; set; } = "";
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public int ChunkSize { get; set; }
    public int TotalParts { get; set; }
    public string RawBucket { get; set; } = "";
    public string RawKey { get; set; } = "";
    public string? S3UploadId { get; set; }

    public UploadState UploadState { get; set; } = UploadState.RECEIVING;
    public QuotaState QuotaState { get; set; } = QuotaState.REQUESTED;
    public ProcessingState? ProcessingState { get; set; }
    public int ProcessingAttempts { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? QueueDeadlineAt { get; set; }
    public DateTime? DeadlineAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Version { get; set; }
}

/// <summary>Parte recebida e gravada no S3 (multipart). Permite retomar o envio.</summary>
public sealed class UploadPart
{
    public string VideoId { get; set; } = "";
    public int PartNumber { get; set; }
    public string ETag { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
}

/// <summary>Histórico das transições da saga: prova o comportamento nos cenários de falha.</summary>
public sealed class VideoHistory
{
    public long Id { get; set; }
    public string VideoId { get; set; } = "";
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? Detail { get; set; }
    public string? MessageId { get; set; }
    public string InstanceId { get; set; } = "";
    public DateTime At { get; set; }
}

/// <summary>Notificação ao usuário (vídeo publicado, falhou, removido).</summary>
public sealed class UserNotification
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string VideoId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Efeito pendente no S3 (apagar o original, o HLS ou abortar o multipart), gravado na mesma
/// transação da transição. O StorageCleanupWorker executa com novas tentativas até dar certo.
/// </summary>
public sealed class StorageCleanup : IVersioned
{
    public long Id { get; set; }
    public string VideoId { get; set; } = "";
    public CleanupAction Action { get; set; }
    public string Bucket { get; set; } = "";
    public string KeyOrPrefix { get; set; } = "";
    public string? UploadId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime NotBefore { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? DoneAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int Version { get; set; }
}
