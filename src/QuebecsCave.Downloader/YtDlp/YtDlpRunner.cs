using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QuebecsCave.Downloader.YtDlp;

public sealed record YtDlpResult(bool Success, int ExitCode, string Stderr, TimeSpan Duration);

public interface IYtDlpRunner
{
    Task<YtDlpResult> DownloadVideoAsync(string vodId, string outputPath, CancellationToken cancellationToken);
}

public sealed class YtDlpRunner : IYtDlpRunner
{
    private readonly DownloaderOptions _options;
    private readonly ILogger<YtDlpRunner> _logger;

    public YtDlpRunner(IOptions<DownloaderOptions> options, ILogger<YtDlpRunner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<YtDlpResult> DownloadVideoAsync(string vodId, string outputPath, CancellationToken cancellationToken)
    {
        if (_options.DryRun)
        {
            _logger.LogInformation("[DryRun] Pretending to download {VodId} → {Path}", vodId, outputPath);
            // Touch a placeholder file so downstream code that wants the path can find one.
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, $"// dry-run placeholder for VOD {vodId}\n", cancellationToken);
            return new YtDlpResult(true, 0, "", TimeSpan.FromMilliseconds(1));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var psi = new ProcessStartInfo
        {
            FileName = _options.YtDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("best");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outputPath);
        psi.ArgumentList.Add("--no-progress");
        psi.ArgumentList.Add("--quiet");
        psi.ArgumentList.Add($"https://www.twitch.tv/videos/{vodId}");

        var sw = Stopwatch.StartNew();
        Process? process = null;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start yt-dlp at '{_options.YtDlpPath}'");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "yt-dlp process failed to start (path: {Path})", _options.YtDlpPath);
            return new YtDlpResult(false, -1, ex.Message, sw.Elapsed);
        }

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await using (cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        }))
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        var stderr = await stderrTask;
        sw.Stop();

        var success = process.ExitCode == 0 && File.Exists(outputPath);
        return new YtDlpResult(success, process.ExitCode, stderr, sw.Elapsed);
    }
}
