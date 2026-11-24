using System.ComponentModel;
using System.Diagnostics;
using Upload.Messaging.Contracts;

namespace MediaProcessor.Processing;

public sealed record ProcessOutput(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Executa ffmpeg/ffprobe. Se o job for cancelado (queda de partição, desligamento, timeout), mata o processo.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessOutput> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ProcessingException(ProcessingFailureReasons.FfmpegError, $"Não foi possível executar {file}: {ex.Message}", retryable: false, ex);
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Já terminou.
            }

            throw;
        }

        return new ProcessOutput(process.ExitCode, await standardOutput, Tail(await standardError, 1500));
    }

    private static string Tail(string text, int maxLength) =>
        text.Length <= maxLength ? text.Trim() : text[^maxLength..].Trim();
}
