using Upload.Data;
using Upload.Messaging.Contracts;

namespace Upload.Api;

/// <summary>POST /api/upload/videos: anuncia o arquivo; os bytes vão depois, em partes.</summary>
public sealed record CreateVideoRequest(
    string? Title,
    string? Visibility,
    string? FileName,
    long SizeBytes,
    string? ContentType);

public sealed record CreateVideoResponse(
    string VideoId,
    string Status,
    long SizeBytes,
    int ChunkSize,
    int TotalParts,
    string PartUrlTemplate,
    string CompleteUrl,
    string StatusUrl)
{
    public static CreateVideoResponse From(VideoUpload video) => new(
        video.VideoId,
        video.Status.ToString(),
        video.SizeBytes,
        video.ChunkSize,
        video.TotalParts,
        $"/api/upload/videos/{video.VideoId}/parts/{{partNumber}}",
        $"/api/upload/videos/{video.VideoId}/complete",
        $"/api/upload/videos/{video.VideoId}");
}

public sealed record PartResponse(string VideoId, int PartNumber, long SizeBytes, string ETag);

/// <summary>Status completo, usado pelo frontend para acompanhar e retomar o envio.</summary>
public sealed record VideoStatusResponse(
    string VideoId,
    string Title,
    string Visibility,
    string Status,
    string? FailureReason,
    string UploadState,
    string QuotaState,
    string? ProcessingState,
    int ProcessingAttempts,
    string FileName,
    long SizeBytes,
    int ChunkSize,
    int TotalParts,
    IReadOnlyList<int> ReceivedParts,
    long UploadedBytes,
    DateTime? QueueDeadlineAt,
    DateTime? DeadlineAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? PublishedAt,
    int? DurationSec,
    IReadOnlyList<Rendition>? Renditions,
    string? PlaybackUrl)
{
    public static VideoStatusResponse From(VideoUpload video, IReadOnlyList<UploadPart> parts) => new(
        video.VideoId,
        video.Title,
        video.Visibility.ToString(),
        video.Status.ToString(),
        video.FailureReason,
        video.UploadState.ToString(),
        video.QuotaState.ToString(),
        video.ProcessingState?.ToString(),
        video.ProcessingAttempts,
        video.FileName,
        video.SizeBytes,
        video.ChunkSize,
        video.TotalParts,
        parts.Select(part => part.PartNumber).Order().ToList(),
        parts.Sum(part => part.SizeBytes),
        video.QueueDeadlineAt,
        video.DeadlineAt,
        video.CreatedAt,
        video.UpdatedAt,
        video.PublishedAt,
        video.DurationSec,
        video.Renditions,
        video.Status == Domain.VideoStatus.PUBLISHED ? $"/api/download/videos/{video.VideoId}/playback" : null);
}

public sealed record VideoSummary(
    string VideoId,
    string Title,
    string Visibility,
    string Status,
    string? FailureReason,
    long SizeBytes,
    DateTime CreatedAt,
    DateTime? PublishedAt,
    int? DurationSec);

public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Page, int Size, long Total);

public sealed record NotificationResponse(long Id, string VideoId, string Type, string Message, DateTime CreatedAt);
