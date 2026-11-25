using Microsoft.EntityFrameworkCore;
using Upload.Configuration;
using Upload.Data;
using Upload.Domain;
using Upload.Messaging;

namespace Upload.Saga;

/// <summary>
/// Varredor de prazos. Roda em todas as réplicas: cada uma busca os vídeos vencidos e aplica a
/// compensação pelo VideoSagaService. Se duas pegarem o mesmo vídeo, o concurrency token deixa
/// uma só compensar; a outra relê, vê FAILED e ignora.
/// </summary>
public sealed class SagaTimeoutWatcher(
    IDbContextFactory<UploadDbContext> factory,
    VideoSagaService saga,
    UploadOptions options,
    ILogger<SagaTimeoutWatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Saga.SweepIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Varredura de prazos falhou; nova tentativa em {Interval}s. erro={Error} instancia={InstanceId}",
                    interval.TotalSeconds, ex.GetBaseException().Message, InstanceInfo.Id);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var idleLimit = now - options.Saga.UploadIdleTimeout;
        var quotaLimit = now - options.Saga.QueueTimeout;

        List<string> candidates;
        await using (var context = await factory.CreateDbContextAsync(cancellationToken))
        {
            candidates = await context.Videos
                .AsNoTracking()
                .Where(video =>
                    (video.Status == VideoStatus.PROCESSING && video.ProcessingState == ProcessingState.QUEUED && video.QueueDeadlineAt < now) ||
                    (video.Status == VideoStatus.PROCESSING && video.ProcessingState == ProcessingState.RUNNING && video.DeadlineAt < now) ||
                    (video.Status == VideoStatus.PENDING && (video.UploadState == UploadState.RECEIVING || video.UploadState == UploadState.COMPLETING) && video.LastActivityAt < idleLimit &&
                        !context.Parts.Any(part => part.VideoId == video.VideoId && part.UploadedAt >= idleLimit)) ||
                    (video.Status == VideoStatus.PENDING && video.QuotaState == QuotaState.REQUESTED && video.CreatedAt < quotaLimit))
                .OrderBy(video => video.UpdatedAt)
                .Select(video => video.VideoId)
                .Take(20)
                .ToListAsync(cancellationToken);
        }

        foreach (var videoId in candidates)
        {
            await saga.HandleCommandAsync(videoId, ownerId: null, new TimeoutCheckEvent(null), cancellationToken);
        }
    }
}
