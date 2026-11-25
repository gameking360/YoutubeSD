using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MediaProcessor.Configuration;
using Upload.Messaging.Contracts;

namespace MediaProcessor.Processing;

/// <summary>Baixa o original de videos-raw e grava o HLS em videos-hls/{videoId}/.</summary>
public sealed class MediaStorage(IAmazonS3 s3, ProcessorOptions options)
{
    public async Task DownloadAsync(string bucket, string key, string destination, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await s3.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key }, cancellationToken);
            await response.WriteResponseStreamToFileAsync(destination, false, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode is "NoSuchKey" or "NoSuchBucket")
        {
            // O Upload já compensou e apagou o original (prazo vencido, cancelado...).
            throw new ProcessingException(ProcessingFailureReasons.SourceNotFound, $"Original s3://{bucket}/{key} não existe mais.", retryable: false, ex);
        }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException or IOException or HttpRequestException)
        {
            throw new ProcessingException(ProcessingFailureReasons.StorageError, $"Falha ao baixar o original: {ex.Message}", retryable: true, ex);
        }
    }

    /// <summary>
    /// Segmentos e thumbnail primeiro; depois as playlists de cada resolução; o master.m3u8 por
    /// último. Assim, se o master existe, o vídeo está completo.
    /// </summary>
    public async Task<int> UploadHlsAsync(string outputDirectory, string bucket, string prefix, CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories).ToList();
        var playlists = files.Where(file => file.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)).ToList();
        var others = files.Except(playlists).ToList();

        try
        {
            await Parallel.ForEachAsync(
                others,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.UploadParallelism), CancellationToken = cancellationToken },
                async (file, token) => await PutAsync(outputDirectory, file, bucket, prefix, token));

            foreach (var playlist in playlists.OrderBy(file => Path.GetFileName(file) == "master.m3u8" ? 1 : 0))
            {
                await PutAsync(outputDirectory, playlist, bucket, prefix, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException or IOException or HttpRequestException)
        {
            throw new ProcessingException(ProcessingFailureReasons.StorageError, $"Falha ao gravar o HLS: {ex.Message}", retryable: true, ex);
        }

        return files.Count;
    }

    private Task PutAsync(string root, string file, string bucket, string prefix, CancellationToken cancellationToken)
    {
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        return s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = prefix + relative,
            FilePath = file,
            ContentType = ContentType(file),
            UseChunkEncoding = false
        }, cancellationToken);
    }

    private static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".m3u8" => "application/vnd.apple.mpegurl",
        ".ts" => "video/mp2t",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream"
    };
}
