using Spectre.Console;
using LustsDepotDownloaderPro.Core;
using LustsDepotDownloaderPro.Utils;

namespace LustsDepotDownloaderPro.UI;

/// <summary>
/// Spectre.Console interactive terminal UI.
/// ONLY instantiated when --terminal-ui true (default for direct CLI use).
/// When invoked from the Electron GUI, --terminal-ui false skips this class
/// entirely so the plain-text progress reporter in Program.cs handles stdout.
/// </summary>
public class TerminalUI
{
    public void ShowHeader()
    {
        AnsiConsole.Write(new Rule("[bold cyan]Lusts Depot Downloader Pro v1.0.0[/]")
            { Justification = Justify.Center });
        AnsiConsole.WriteLine();
    }

    public void ShowStatus(string message) =>
        AnsiConsole.MarkupLine($"[cyan]ℹ  {EscapeMarkup(message)}[/]");

    public async Task RunDownloadWithProgressAsync(DownloadEngine engine)
    {
        try
        {
            await AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var dlTask    = ctx.AddTask("[cyan]Downloading[/]",    maxValue: 100);
                    var speedTask = ctx.AddTask("[yellow]Speed[/]",        maxValue: 1, autoStart: false);
                    speedTask.IsIndeterminate = true;

                    engine.ProgressChanged += (_, e) =>
                    {
                        dlTask.Value       = e.PercentComplete;
                        dlTask.Description = $"[cyan]Downloading {EscapeMarkup(e.CurrentFile ?? "...")}[/]";
                        speedTask.Description = $"[yellow]Speed: {e.SpeedMBps:F2} MB/s" +
                            (e.EtaSeconds > 0 ? $"  ETA {FormatEta(e.EtaSeconds)}" : "") + "[/]";
                    };

                    await engine.RunAsync();

                    dlTask.Value = 100;
                    dlTask.StopTask();
                    speedTask.StopTask();
                });
        }
        catch (OperationCanceledException)
        {
            // User pressed Ctrl+C - this is expected, don't re-throw
            // The engine has already saved the checkpoint
        }
    }

    public void ShowSummary(Models.DownloadStatistics stats)
    {
        var t = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .AddColumn("Metric").AddColumn("Value");

        t.AddRow("Downloaded",  $"[green]{stats.DownloadedMB:F2} MB[/]");
        t.AddRow("Total",       $"{stats.TotalMB:F2} MB");
        t.AddRow("Progress",    $"[cyan]{stats.Percent:F1}%[/]");
        t.AddRow("Speed",       $"[yellow]{stats.SpeedMBps:F2} MB/s[/]");
        t.AddRow("Chunks",      $"{stats.CompletedChunks}/{stats.TotalChunks}");
        t.AddRow("Status",      stats.IsCompleted ? "[green]✓ Complete[/]" :
                                stats.IsPaused    ? "[yellow]⏸  Paused[/]" : "[cyan]Running[/]");
        AnsiConsole.Write(t);
    }

    private static string FormatEta(double sec)
    {
        if (sec <= 0) return "--:--:--";
        var t = TimeSpan.FromSeconds(sec);
        return $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
    }

    private static string EscapeMarkup(string s) =>
        s.Replace("[", "[[").Replace("]", "]]");
}
