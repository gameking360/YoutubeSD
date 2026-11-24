using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Upload.Messaging.Storage;

namespace Upload.Storage;

/// <summary>
/// Original do vídeo em videos-raw/{videoId}/original.{ext}, montado por S3 multipart: cada parte
/// recebida pela API vira um UploadPart, e reenviar a mesma parte só a sobrescreve (idempotente).
/// </summary>
public sealed class RawStorage(IAmazonS3 s3, S3Settings settings)
{
    public string Bucket => settings.RawBucket;

    public async Task<string> StartAsync(string key, string? contentType, CancellationToken cancellationToken)
    {
        var response = await s3.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = Bucket,
            Key = key,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
        }, cancellationToken);

        return response.UploadId;
    }

    public async Task<string> UploadPartAsync(string key, string uploadId, int partNumber, Stream content, long length, CancellationToken cancellationToken)
    {
        var response = await s3.UploadPartAsync(new UploadPartRequest
        {
            BucketName = Bucket,
            Key = key,
            UploadId = uploadId,
            PartNumber = partNumber,
            InputStream = content,
            PartSize = length,
            UseChunkEncoding = false
        }, cancellationToken);

        return response.ETag;
    }

    /// <summary>
    /// Monta o arquivo final. É idempotente: se outra réplica já completou (NoSuchUpload), confere
    /// que o objeto existe com o tamanho esperado.
    /// </summary>
    public async Task CompleteAsync(
        string key,
        string uploadId,
        IEnumerable<(int Number, string ETag)> parts,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        try
        {
            await s3.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
            {
                BucketName = Bucket,
                Key = key,
                UploadId = uploadId,
                PartETags = parts.OrderBy(part => part.Number).Select(part => new PartETag(part.Number, part.ETag)).ToList()
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchUpload")
        {
            // Já completado (ex.: o cliente repetiu o complete em outra réplica): confere abaixo.
        }

        var size = await SizeAsync(key, cancellationToken);
        if (size != expectedSize)
        {
            throw new RawUploadIncompleteException(
                size is null
                    ? "O arquivo não foi montado no armazenamento."
                    : $"O arquivo montado tem {size} bytes, mas eram esperados {expectedSize}.");
        }
    }

    public async Task<long?> SizeAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await s3.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = Bucket, Key = key }, cancellationToken);
            return metadata.ContentLength;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task AbortQuietlyAsync(string key, string uploadId)
    {
        try
        {
            await s3.AbortMultipartUploadAsync(new AbortMultipartUploadRequest { BucketName = Bucket, Key = key, UploadId = uploadId });
        }
        catch (Exception)
        {
            // Melhor esforço: um multipart órfão não aparece como objeto.
        }
    }
}

public sealed class RawUploadIncompleteException(string message) : Exception(message);
