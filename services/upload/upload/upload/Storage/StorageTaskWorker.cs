using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Upload.Data;
using Upload.Domain;
using Upload.Messaging;
using Upload.Saga;

namespace Upload.Storage;

/// <summary>
/// Executa os efeitos no S3 decididos pela saga (tabela storage_cleanup): montar o original a partir
/// das partes, abortar o multipart, apagar o original e apagar o HLS. Roda em todas as réplicas:
/// cada tarefa é reivindicada com concurrency token, e tudo é idempotente. Se a réplica morre no
/// meio, a reivindicação expira e outra assume. Falhou? Nova tentativa com backoff, até o S3 voltar.
/// </summary>
public sealed class StorageTaskWorker(
    IDbContextFactory<UploadDbContext> factory,
    IAmazonS3 s3,
    RawStorage raw,
    VideoSagaService saga,
    ILogger<StorageTaskWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                worked = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Limpeza do S3 falhou; nova tentativa. erro={Error} instancia={InstanceId}",
                    ex.GetBaseException().Message, InstanceInfo.Id);
            }

            if (!worked)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        List<long> pending;
        await using (var context = await factory.CreateDbContextAsync(cancellationToken))
        {
            pending = await context.StorageCleanups
                .AsNoTracking()
                .Where(task => task.DoneAt == null && task.NotBefore <= now && (task.LockedUntil == null || task.LockedUntil < now))
                .OrderBy(task => task.Id)
                .Select(task => task.Id)
                .Take(10)
                .ToListAsync(cancellationToken);
        }

        foreach (var id in pending)
        {
            var task = await ClaimAsync(id, cancellationToken);
            if (task is not null)
            {
                await ExecuteTaskAsync(task, cancellationToken);
            }
        }

        return pending.Count > 0;
    }

    private async Task<StorageCleanup?> ClaimAsync(long id, CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var task = await context.StorageCleanups.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null || task.DoneAt is not null || task.LockedUntil > now)
        {
            return null;
        }

        task.LockedBy = InstanceInfo.Id;
        task.LockedUntil = now + ClaimDuration;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return task;
        }
        catch (DbUpdateConcurrencyException)
        {
            return null; // Outra réplica reivindicou primeiro.
        }
    }

    private async Task ExecuteTaskAsync(StorageCleanup task, CancellationToken cancellationToken)
    {
        string? error = null;
        var removed = 0;

        try
        {
            removed = task.Action switch
            {
                CleanupAction.COMPLETE_MULTIPART => await CompleteMultipartAsync(task, cancellationToken),
                CleanupAction.ABORT_MULTIPART => await AbortMultipartAsync(task, cancellationToken),
                CleanupAction.DELETE_OBJECT => await DeleteObjectAsync(task.Bucket, task.KeyOrPrefix, cancellationToken),
                CleanupAction.DELETE_PREFIX => await DeletePrefixAsync(task.Bucket, task.KeyOrPrefix, cancellationToken),
                _ => 0
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
        }

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var current = await context.StorageCleanups.SingleAsync(t => t.Id == task.Id, cancellationToken);
        var now = DateTime.UtcNow;
        current.LockedBy = null;
        current.LockedUntil = null;
        current.Attempts++;

        if (error is null)
        {
            current.DoneAt = now;
            current.LastError = null;
            logger.LogInformation(
                "Limpeza no S3 concluída acao={Action} s3://{Bucket}/{Key} objetos={Count} videoId={VideoId} motivo={Reason} instancia={InstanceId}",
                task.Action, task.Bucket, task.KeyOrPrefix, removed, task.VideoId, task.Reason, InstanceInfo.Id);
        }
        else
        {
            var backoff = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Min(current.Attempts, 8))));
            current.NotBefore = now + backoff;
            current.LastError = error.Length <= 500 ? error : error[..500];
            logger.LogWarning(
                "Limpeza no S3 falhou acao={Action} s3://{Bucket}/{Key} nova tentativa em {Backoff}s erro={Error} instancia={InstanceId}",
                task.Action, task.Bucket, task.KeyOrPrefix, backoff.TotalSeconds, error, InstanceInfo.Id);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Monta o original (CompleteMultipartUpload, idempotente) e avisa a saga, que dispara o
    /// video.uploaded se a cota já estiver reservada.
    /// </summary>
    private async Task<int> CompleteMultipartAsync(StorageCleanup task, CancellationToken cancellationToken)
    {
        VideoUpload? video;
        List<UploadPart> parts;
        await using (var context = await factory.CreateDbContextAsync(cancellationToken))
        {
            video = await context.Videos.AsNoTracking().SingleOrDefaultAsync(v => v.VideoId == task.VideoId, cancellationToken);
            if (video is null || video.Status != VideoStatus.PENDING || video.UploadState != UploadState.COMPLETING || task.UploadId is null)
            {
                // A saga já seguiu (cancelado, abortado ou montado por outra réplica).
                return 0;
            }

            parts = await context.Parts.AsNoTracking().Where(part => part.VideoId == task.VideoId).ToListAsync(cancellationToken);
        }

        var started = DateTime.UtcNow;
        await raw.CompleteAsync(video.RawKey, task.UploadId, parts.Select(part => (part.PartNumber, part.ETag)), video.SizeBytes, cancellationToken);
        logger.LogInformation(
            "Original montado no S3 videoId={VideoId} bytes={Size} partes={Parts} em {Seconds:0.#}s correlationId={CorrelationId} instancia={InstanceId}",
            video.VideoId, video.SizeBytes, parts.Count, (DateTime.UtcNow - started).TotalSeconds, video.VideoId, InstanceInfo.Id);

        await saga.HandleCommandAsync(task.VideoId, ownerId: null, new UploadCompletedEvent(), cancellationToken);
        return 1;
    }

    private async Task<int> AbortMultipartAsync(StorageCleanup task, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(task.UploadId))
        {
            try
            {
                await s3.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
                {
                    BucketName = task.Bucket,
                    Key = task.KeyOrPrefix,
                    UploadId = task.UploadId
                }, cancellationToken);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchUpload" || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Já abortado ou já completado.
            }
        }

        // Se o complete correu ao mesmo tempo que o abort, o objeto pode ter sido montado.
        return await DeleteObjectAsync(task.Bucket, task.KeyOrPrefix, cancellationToken);
    }

    private async Task<int> DeleteObjectAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        await s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = key }, cancellationToken);
        return 1;
    }

    private async Task<int> DeletePrefixAsync(string bucket, string prefix, CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        string? continuation = null;
        do
        {
            var page = await s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucket,
                Prefix = prefix,
                ContinuationToken = continuation
            }, cancellationToken);

            keys.AddRange((page.S3Objects ?? []).Select(item => item.Key));
            continuation = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (continuation is not null);

        await Parallel.ForEachAsync(
            keys,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
            async (key, token) => await s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = key }, token));

        return keys.Count;
    }
}
