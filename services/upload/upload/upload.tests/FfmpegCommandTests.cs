using System.Text.RegularExpressions;
using MediaProcessor.Configuration;
using MediaProcessor.Processing;
using Upload.Messaging.Contracts;

namespace Upload.Tests;

public sealed class FfmpegCommandTests
{
    private readonly ProcessorOptions _options = new();

    [Fact]
    public void Renditions_360p_e_720p_com_largura_par()
    {
        var media = new MediaInfo(20, 1920, 1080, 30, HasAudio: true);

        var renditions = FfmpegCommand.Renditions(media);

        Assert.Equal(
            [new Rendition("360p", 640, 360, 928000), new Rendition("720p", 1280, 720, 2928000)],
            renditions);
    }

    [Theory]
    [InlineData(1080, 1920, 360, 202)]
    [InlineData(1000, 750, 360, 480)]
    [InlineData(853, 480, 360, 640)]
    public void Largura_sempre_par(int width, int height, int target, int expected)
    {
        var even = FfmpegCommand.EvenWidth(width, height, target);
        Assert.Equal(expected, even);
        Assert.Equal(0, even % 2);
    }

    [Fact]
    public void Comando_hls_usa_gop_fixo_alinhado_e_var_stream_map()
    {
        var media = new MediaInfo(20, 1280, 720, 30, HasAudio: true);
        var args = FfmpegCommand.Hls("in.mp4", "out", media, _options);

        Assert.Equal("120", After(args, "-g"));
        Assert.Equal("120", After(args, "-keyint_min"));
        Assert.Equal("0", After(args, "-sc_threshold"));
        Assert.Equal("4", After(args, "-hls_time"));
        Assert.Equal("vod", After(args, "-hls_playlist_type"));
        Assert.Equal("master.m3u8", After(args, "-master_pl_name"));
        Assert.Equal("v:0,a:0,name:360p v:1,a:1,name:720p", After(args, "-var_stream_map"));
        Assert.Equal("libx264", After(args, "-c:v"));
        Assert.Equal("aac", After(args, "-c:a"));
        Assert.Equal(Path.Combine("out", "%v", "index.m3u8"), args[^1]);
        Assert.Equal(Path.Combine("out", "%v", "seg_%05d.ts"), After(args, "-hls_segment_filename"));
    }

    [Fact]
    public void Video_sem_audio_nao_mapeia_audio()
    {
        var media = new MediaInfo(20, 1280, 720, 25, HasAudio: false);
        var args = FfmpegCommand.Hls("in.mp4", "out", media, _options);

        Assert.DoesNotContain("0:a:0", args);
        Assert.DoesNotContain("-c:a", args);
        Assert.Equal("v:0,name:360p v:1,name:720p", After(args, "-var_stream_map"));
        Assert.Equal("100", After(args, "-g"));
    }

    [Fact]
    public void Nomes_dos_segmentos_batem_com_o_regex_do_download()
    {
        // HlsController do Download: [A-Za-z0-9_-]+\.ts
        var segment = string.Format("seg_{0:D5}.ts", 0);
        Assert.Matches(new Regex(@"^[A-Za-z0-9_-]+\.ts$"), segment);
    }

    [Fact]
    public void Ffprobe_com_rotacao_troca_largura_e_altura()
    {
        const string json = """
        {
          "streams": [
            { "codec_type": "video", "width": 1920, "height": 1080, "avg_frame_rate": "30000/1001",
              "side_data_list": [ { "rotation": -90 } ] },
            { "codec_type": "audio" }
          ],
          "format": { "duration": "12.345" }
        }
        """;

        var media = MediaInfo.Parse(json);

        Assert.Equal(1080, media.Width);
        Assert.Equal(1920, media.Height);
        Assert.True(media.HasAudio);
        Assert.Equal(13, media.DurationSec);
        Assert.Equal(29.97, media.FrameRate, 2);
    }

    [Fact]
    public void Ffprobe_sem_video_e_midia_invalida_sem_nova_tentativa()
    {
        const string json = """{ "streams": [ { "codec_type": "audio" } ], "format": { "duration": "3.0" } }""";

        var error = Assert.Throws<ProcessingException>(() => MediaInfo.Parse(json));

        Assert.Equal(ProcessingFailureReasons.InvalidMedia, error.Reason);
        Assert.False(error.Retryable);
    }

    [Fact]
    public void Duracao_minima_e_um_segundo()
    {
        const string json = """{ "streams": [ { "codec_type": "video", "width": 320, "height": 240 } ] }""";
        Assert.Equal(1, MediaInfo.Parse(json).DurationSec);
    }

    private static string After(IReadOnlyList<string> args, string flag)
    {
        var index = args.ToList().IndexOf(flag);
        Assert.True(index >= 0, $"{flag} ausente");
        return args[index + 1];
    }
}
