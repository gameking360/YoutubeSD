namespace Upload.Configuration;

/// <summary>Configuração do Upload (seção "Upload"); tudo pode vir de variáveis de ambiente (Upload__*).</summary>
public sealed class UploadOptions
{
    public const long MiB = 1024 * 1024;

    /// <summary>Tamanho de cada parte. Partes de 8 MiB passam pelo limite padrão do gateway.</summary>
    public int ChunkSizeBytes { get; set; } = (int)(8 * MiB);

    public long MaxFileSizeBytes { get; set; } = 10L * 1024 * MiB;

    /// <summary>Partes recebidas ao mesmo tempo por instância; limita a memória (escala vertical).</summary>
    public int MaxConcurrentParts { get; set; } = 8;

    /// <summary>Extensões aceitas, separadas por vírgula.</summary>
    public string AllowedExtensions { get; set; } = "mp4,mov,mkv,webm,avi,m4v";

    public SagaOptions Saga { get; set; } = new();

    public IReadOnlySet<string> Extensions =>
        AllowedExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.TrimStart('.').ToLowerInvariant())
            .ToHashSet();
}

/// <summary>Prazos da saga.</summary>
public sealed class SagaOptions
{
    /// <summary>Tempo máximo esperando um media-processor pegar o vídeo (nenhuma réplica disponível).</summary>
    public int QueueTimeoutMinutes { get; set; } = 30;

    /// <summary>Prazo de processamento = base + segundos por MB, até o máximo.</summary>
    public int ProcessingBaseSeconds { get; set; } = 60;

    public double ProcessingSecondsPerMb { get; set; } = 1;

    public int ProcessingMaxMinutes { get; set; } = 120;

    /// <summary>Upload sem nenhuma parte nova por este tempo é considerado abandonado.</summary>
    public int UploadIdleTimeoutMinutes { get; set; } = 60;

    public int SweepIntervalSeconds { get; set; } = 5;

    public TimeSpan QueueTimeout => TimeSpan.FromMinutes(Math.Max(1, QueueTimeoutMinutes));

    public TimeSpan UploadIdleTimeout => TimeSpan.FromMinutes(Math.Max(1, UploadIdleTimeoutMinutes));

    public TimeSpan ProcessingTimeout(long sizeBytes)
    {
        var megabytes = sizeBytes / (double)UploadOptions.MiB;
        var seconds = ProcessingBaseSeconds + ProcessingSecondsPerMb * megabytes;
        return TimeSpan.FromSeconds(Math.Min(seconds, ProcessingMaxMinutes * 60.0));
    }
}
