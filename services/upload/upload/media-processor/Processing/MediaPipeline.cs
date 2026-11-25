using MediaProcessor.Configuration;
using Upload.Messaging;
using Upload.Messaging.Contracts;

namespace MediaProcessor.Processing;

public sealed record PipelineResult(int DurationSec, IReadOnlyList<Rendition> Renditions, string MasterKey, string? ThumbnailKey, int Files);

/// <summary>
/// Uma tentativa de processamento: dependência simulada (R5) → baixa o original → ffprobe →
/// ffmpeg (HLS 360p/720p) → thumbnail → grava no S3. Usa só disco temporário, então vídeo
/// longo ou curto passam pelo mesmo caminho.
/// </summary>
public sealed class MediaPipeline(
    ProcessorOptions options,
    MediaStorage storage,
    ChaosSettingsProvider chaos,
    ILogger<MediaPipeline> logger)
{
    public async Task<PipelineResult> RunAsync(VideoUploadedPayload job, int attempt, CancellationToken cancellationToken)
    {
        var settings = await chaos.GetAsync(cancellationToken);
        if (settings.FailureRate > 0 && Random.Shared.NextDouble() < settings.FailureRate)
        {
            throw new ProcessingException(ProcessingFailureReasons.SimulatedFailure,
                $"Falha simulada da dependência (taxa {settings.FailureRate:0.##}, origem {settings.Source}).", retryable: true);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, options.JobTimeoutMinutes)));
        var token = timeout.Token;

        var work = Path.Combine(options.WorkDirectory, $"{job.VideoId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        try
        {
            var input = Path.Combine(work, "input" + Path.GetExtension(job.RawKey));
            await storage.DownloadAsync(job.RawBucket, job.RawKey, input, token);

            // Atraso depois de baixar o original: se o prazo vencer agora, o resultado ainda sai,
            // só que tarde (F7), e o Upload descarta.
            if (settings.LatencyMs > 0)
            {
                logger.LogInformation(
                    "Dependência lenta (R5): aguardando {Latency}ms (origem {Source}) videoId={VideoId} tentativa={Attempt} instancia={InstanceId}",
                    settings.LatencyMs, settings.Source, job.VideoId, attempt, InstanceInfo.Id);
                await Task.Delay(settings.LatencyMs, token);
            }

            var probe = await ProcessRunner.RunAsync(options.FfprobePath, FfmpegCommand.Probe(input), token);
            if (probe.ExitCode != 0)
            {
                throw new ProcessingException(ProcessingFailureReasons.InvalidMedia, $"ffprobe recusou o arquivo: {probe.StandardError}", retryable: false);
            }

            var media = MediaInfo.Parse(probe.StandardOutput);
            var renditions = FfmpegCommand.Renditions(media);
            logger.LogInformation(
                "Transcodificando videoId={VideoId} {Width}x{Height} {Fps:0.##}fps {Duration:0.#}s audio={HasAudio} → {Renditions} tentativa={Attempt} instancia={InstanceId}",
                job.VideoId, media.Width, media.Height, media.FrameRate, media.DurationSeconds, media.HasAudio,
                string.Join(", ", renditions.Select(r => $"{r.Name} {r.Width}x{r.Height}")), attempt, InstanceInfo.Id);

            var output = Path.Combine(work, "out");
            Directory.CreateDirectory(output);
            foreach (var rendition in renditions)
            {
                Directory.CreateDirectory(Path.Combine(output, rendition.Name));
            }

            var started = DateTime.UtcNow;
            var ffmpeg = await ProcessRunner.RunAsync(options.FfmpegPath, FfmpegCommand.Hls(input, output, media, options), token);
            if (ffmpeg.ExitCode != 0)
            {
                throw new ProcessingException(ProcessingFailureReasons.FfmpegError, $"ffmpeg saiu com código {ffmpeg.ExitCode}: {ffmpeg.StandardError}", retryable: true);
            }

            EnsureOutputs(output, renditions);

            string? thumbnailKey = null;
            var thumbnail = await ProcessRunner.RunAsync(options.FfmpegPath, FfmpegCommand.Thumbnail(input, Path.Combine(output, "thumb.jpg"), media), token);
            if (thumbnail.ExitCode == 0 && File.Exists(Path.Combine(output, "thumb.jpg")))
            {
                thumbnailKey = job.HlsPrefix + "thumb.jpg";
            }
            else
            {
                logger.LogWarning("Thumbnail não gerada para {VideoId}: {Error}", job.VideoId, thumbnail.StandardError);
            }

            // O input não vai para o S3.
            File.Delete(input);
            var files = await storage.UploadHlsAsync(output, job.HlsBucket, job.HlsPrefix, token);

            logger.LogInformation(
                "HLS gravado em s3://{Bucket}/{Prefix} arquivos={Files} transcodificação={Seconds:0.#}s videoId={VideoId} instancia={InstanceId}",
                job.HlsBucket, job.HlsPrefix, files, (DateTime.UtcNow - started).TotalSeconds, job.VideoId, InstanceInfo.Id);

            return new PipelineResult(media.DurationSec, renditions, job.HlsPrefix + "master.m3u8", thumbnailKey, files);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ProcessingException(ProcessingFailureReasons.Timeout, $"O processamento passou de {options.JobTimeoutMinutes} minutos.", retryable: true);
        }
        finally
        {
            TryDelete(work);
        }
    }

    private static void EnsureOutputs(string output, IReadOnlyList<Rendition> renditions)
    {
        var expected = new[] { Path.Combine(output, "master.m3u8") }
            .Concat(renditions.Select(rendition => Path.Combine(output, rendition.Name, "index.m3u8")));

        var missing = expected.Where(path => !File.Exists(path)).ToList();
        if (missing.Count > 0)
        {
            throw new ProcessingException(ProcessingFailureReasons.FfmpegError,
                $"ffmpeg não gerou {string.Join(", ", missing.Select(Path.GetFileName))}.", retryable: true);
        }
    }

    private void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Não foi possível apagar {Directory}: {Error}", directory, ex.Message);
        }
    }
}
