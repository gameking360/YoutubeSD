using System.Globalization;

namespace MediaProcessor.Configuration;

/// <summary>Configuração do media-processor (seção "Processor" + variáveis PROCESSOR_*).</summary>
public sealed class ProcessorOptions
{
    /// <summary>Jobs em paralelo por instância (escala vertical). O total fica limitado pelas partições do tópico.</summary>
    public int Concurrency { get; set; } = 1;

    /// <summary>Tentativas por vídeo, somadas entre réplicas (sobrevivem a quedas).</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Threads do FFmpeg: 0 = todos os núcleos do container (escala vertical).</summary>
    public int FfmpegThreads { get; set; }

    public string Preset { get; set; } = "veryfast";

    /// <summary>Duração dos segmentos HLS (4 a 6 s).</summary>
    public int SegmentSeconds { get; set; } = 4;

    /// <summary>Limite de um job; protege contra FFmpeg travado.</summary>
    public int JobTimeoutMinutes { get; set; } = 120;

    public int UploadParallelism { get; set; } = 8;

    public string WorkDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "media");

    public string FfmpegPath { get; set; } = "ffmpeg";

    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>R5: atraso artificial da dependência (PROCESSOR_LATENCY_MS).</summary>
    public int LatencyMs { get; set; }

    /// <summary>R5: probabilidade de falha da dependência, de 0 a 1 (PROCESSOR_FAILURE_RATE).</summary>
    public double FailureRate { get; set; }

    public static ProcessorOptions Resolve(IConfiguration configuration)
    {
        var options = configuration.GetSection("Processor").Get<ProcessorOptions>() ?? new ProcessorOptions();

        if (int.TryParse(configuration["PROCESSOR_LATENCY_MS"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var latency))
        {
            options.LatencyMs = latency;
        }

        if (double.TryParse(configuration["PROCESSOR_FAILURE_RATE"], NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
        {
            options.FailureRate = rate;
        }

        options.Concurrency = Math.Max(1, options.Concurrency);
        options.MaxAttempts = Math.Max(1, options.MaxAttempts);
        options.SegmentSeconds = Math.Clamp(options.SegmentSeconds, 4, 6);
        options.LatencyMs = Math.Max(0, options.LatencyMs);
        options.FailureRate = Math.Clamp(options.FailureRate, 0, 1);
        return options;
    }
}
