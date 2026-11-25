using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;

namespace Upload.Messaging.Storage;

/// <summary>Endpoint e credenciais do S3 (MiniStack), sempre por configuração.</summary>
public sealed class S3Settings
{
    public string ServiceUrl { get; set; } = "http://localhost:4566";
    public string Region { get; set; } = "us-east-1";
    public string AccessKey { get; set; } = "test";
    public string SecretKey { get; set; } = "test";
    public string RawBucket { get; set; } = "videos-raw";
    public string HlsBucket { get; set; } = "videos-hls";

    public static S3Settings Resolve(IConfiguration configuration)
    {
        var settings = configuration.GetSection("S3").Get<S3Settings>() ?? new S3Settings();
        settings.ServiceUrl = configuration["AWS_ENDPOINT_URL"] is { Length: > 0 } url ? url : settings.ServiceUrl;
        settings.Region = configuration["AWS_REGION"] is { Length: > 0 } region ? region : settings.Region;
        settings.AccessKey = configuration["AWS_ACCESS_KEY_ID"] is { Length: > 0 } key ? key : settings.AccessKey;
        settings.SecretKey = configuration["AWS_SECRET_ACCESS_KEY"] is { Length: > 0 } secret ? secret : settings.SecretKey;
        return settings;
    }

    public IAmazonS3 CreateClient()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = ServiceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = Region,
            // O SDK v4 manda checksums "trailing" por padrão; emuladores de S3 costumam recusar.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromMinutes(2),
            MaxErrorRetry = 3
        };

        return new AmazonS3Client(new BasicAWSCredentials(AccessKey, SecretKey), config);
    }
}
