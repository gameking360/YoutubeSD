using System.Globalization;
using System.Text.Json;
using Upload.Messaging.Contracts;

namespace MediaProcessor.Processing;

/// <summary>O que o ffprobe descobriu sobre o original (já considerando rotação de celular).</summary>
public sealed record MediaInfo(double DurationSeconds, int Width, int Height, double FrameRate, bool HasAudio)
{
    /// <summary>durationSec do contrato: inteiro, no mínimo 1.</summary>
    public int DurationSec => Math.Max(1, (int)Math.Ceiling(DurationSeconds));

    /// <summary>Lê a saída de "ffprobe -print_format json -show_streams -show_format".</summary>
    public static MediaInfo Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        JsonElement? video = null;
        var hasAudio = false;
        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var type = stream.TryGetProperty("codec_type", out var codecType) ? codecType.GetString() : null;
                if (type == "video" && video is null && !IsAttachedPicture(stream))
                {
                    video = stream;
                }
                else if (type == "audio")
                {
                    hasAudio = true;
                }
            }
        }

        if (video is not { } videoStream)
        {
            throw new ProcessingException(ProcessingFailureReasons.InvalidMedia, "O arquivo não tem uma faixa de vídeo.", retryable: false);
        }

        var width = videoStream.TryGetProperty("width", out var w) && w.TryGetInt32(out var widthValue) ? widthValue : 0;
        var height = videoStream.TryGetProperty("height", out var h) && h.TryGetInt32(out var heightValue) ? heightValue : 0;
        if (width <= 0 || height <= 0)
        {
            throw new ProcessingException(ProcessingFailureReasons.InvalidMedia, "Não foi possível ler a resolução do vídeo.", retryable: false);
        }

        if (Math.Abs(Rotation(videoStream)) % 180 == 90)
        {
            (width, height) = (height, width);
        }

        var frameRate = ParseFrameRate(videoStream, "avg_frame_rate") ?? ParseFrameRate(videoStream, "r_frame_rate") ?? 30;
        if (frameRate is < 1 or > 240)
        {
            frameRate = 30;
        }

        var duration = Duration(root.TryGetProperty("format", out var format) ? format : default)
            ?? Duration(videoStream)
            ?? 0;

        return new MediaInfo(duration, width, height, frameRate, hasAudio);
    }

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var disposition) &&
        disposition.TryGetProperty("attached_pic", out var attached) &&
        attached.TryGetInt32(out var value) && value == 1;

    private static int Rotation(JsonElement stream)
    {
        if (stream.TryGetProperty("side_data_list", out var sideData) && sideData.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sideData.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var rotation) && rotation.TryGetDouble(out var degrees))
                {
                    return (int)Math.Round(degrees);
                }
            }
        }

        if (stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("rotate", out var rotate) &&
            int.TryParse(rotate.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tagDegrees))
        {
            return tagDegrees;
        }

        return 0;
    }

    private static double? ParseFrameRate(JsonElement stream, string property)
    {
        if (!stream.TryGetProperty(property, out var value) || value.GetString() is not { } text)
        {
            return null;
        }

        var parts = text.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            denominator > 0 && numerator > 0)
        {
            return numerator / denominator;
        }

        return null;
    }

    private static double? Duration(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("duration", out var value))
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? seconds
            : null;
    }
}
