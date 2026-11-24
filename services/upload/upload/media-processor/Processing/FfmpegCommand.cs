using System.Globalization;
using MediaProcessor.Configuration;
using Upload.Messaging.Contracts;

namespace MediaProcessor.Processing;

/// <summary>Uma resolução da escada HLS: altura fixa, largura pela proporção do original.</summary>
public sealed record LadderStep(string Name, int Height, int VideoKbps, int AudioKbps)
{
    public int Bandwidth => (VideoKbps + AudioKbps) * 1000;
}

/// <summary>
/// Monta os argumentos do FFmpeg. Um único comando gera 360p e 720p com -var_stream_map
/// (H.264 + AAC), segmentos de 4 a 6 s, GOP fixo e alinhado entre as resoluções
/// (-g/-keyint_min + -sc_threshold 0) e manifestos com caminhos relativos:
/// <code>
/// out/master.m3u8
/// out/{360p|720p}/index.m3u8
/// out/{360p|720p}/seg_00000.ts
/// </code>
/// É exatamente o layout que o Download lê em videos-hls/{videoId}/.
/// </summary>
public static class FfmpegCommand
{
    public const int AudioKbps = 128;

    public static readonly IReadOnlyList<LadderStep> Ladder =
    [
        new("360p", 360, 800, AudioKbps),
        new("720p", 720, 2800, AudioKbps)
    ];

    /// <summary>Largura par que mantém a proporção do original.</summary>
    public static int EvenWidth(int sourceWidth, int sourceHeight, int targetHeight)
    {
        var width = (int)Math.Round(sourceWidth * (double)targetHeight / sourceHeight);
        return Math.Max(2, (width + 1) / 2 * 2);
    }

    public static IReadOnlyList<Rendition> Renditions(MediaInfo media) =>
        Ladder.Select(step => new Rendition(step.Name, EvenWidth(media.Width, media.Height, step.Height), step.Height,
                media.HasAudio ? step.Bandwidth : step.VideoKbps * 1000))
            .ToList();

    public static IReadOnlyList<string> Hls(string input, string outputDirectory, MediaInfo media, ProcessorOptions options)
    {
        var segment = options.SegmentSeconds;
        var gop = Math.Max(1, (int)Math.Round(media.FrameRate * segment));
        var renditions = Renditions(media);
        var invariant = CultureInfo.InvariantCulture;

        var split = string.Join("", renditions.Select((_, index) => $"[s{index}]"));
        var scales = string.Join(";", renditions.Select((rendition, index) =>
            $"[s{index}]scale={rendition.Width}:{rendition.Height},setsar=1[v{index}]"));

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-i", input,
            "-filter_complex", $"[0:v]split={renditions.Count}{split};{scales}"
        };

        for (var index = 0; index < renditions.Count; index++)
        {
            args.AddRange(["-map", $"[v{index}]"]);
            if (media.HasAudio)
            {
                args.AddRange(["-map", "0:a:0"]);
            }
        }

        args.AddRange(
        [
            "-c:v", "libx264", "-preset", options.Preset, "-profile:v", "main", "-pix_fmt", "yuv420p",
            "-g", gop.ToString(invariant), "-keyint_min", gop.ToString(invariant), "-sc_threshold", "0",
            "-force_key_frames", $"expr:gte(t,n_forced*{segment})"
        ]);

        for (var index = 0; index < Ladder.Count; index++)
        {
            var kbps = Ladder[index].VideoKbps;
            args.AddRange(
            [
                $"-b:v:{index}", $"{kbps}k",
                $"-maxrate:v:{index}", $"{kbps * 107 / 100}k",
                $"-bufsize:v:{index}", $"{kbps * 3 / 2}k"
            ]);
        }

        if (media.HasAudio)
        {
            args.AddRange(["-c:a", "aac", "-b:a", $"{AudioKbps}k", "-ac", "2", "-ar", "48000"]);
        }

        args.AddRange(
        [
            "-threads", options.FfmpegThreads.ToString(invariant),
            "-f", "hls",
            "-hls_time", segment.ToString(invariant),
            "-hls_playlist_type", "vod",
            "-hls_flags", "independent_segments",
            "-hls_segment_filename", Path.Combine(outputDirectory, "%v", "seg_%05d.ts"),
            "-master_pl_name", "master.m3u8",
            "-var_stream_map", StreamMap(renditions, media.HasAudio),
            Path.Combine(outputDirectory, "%v", "index.m3u8")
        ]);

        return args;
    }

    public static string StreamMap(IReadOnlyList<Rendition> renditions, bool hasAudio) =>
        string.Join(" ", renditions.Select((rendition, index) =>
            hasAudio ? $"v:{index},a:{index},name:{rendition.Name}" : $"v:{index},name:{rendition.Name}"));

    public static IReadOnlyList<string> Thumbnail(string input, string output, MediaInfo media) =>
    [
        "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
        "-ss", Math.Min(1.0, media.DurationSeconds / 2).ToString("0.###", CultureInfo.InvariantCulture),
        "-i", input,
        "-frames:v", "1",
        "-vf", "scale=640:-2",
        "-q:v", "3",
        output
    ];

    public static IReadOnlyList<string> Probe(string input) =>
        ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", input];
}
