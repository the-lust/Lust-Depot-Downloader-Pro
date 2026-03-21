using Spectre.Console;
using LustsDepotDownloaderPro.Core;
using LustsDepotDownloaderPro.Utils;
using LustsDepotDownloaderPro.Models;

namespace LustsDepotDownloaderPro.UI;

/// <summary>
/// Clean release-mode UI.
/// Shows: game name, progress bar, %, speed, ETA.
/// All Logger.Info/Warn traffic is silenced to file-only while rendering.
/// </summary>
public class TerminalUI
{
    private string _appName = "";

    public void ShowHeader()
    {
        AnsiConsole.Write(new Rule("[bold cyan]Lusts Depot Downloader Pro v1.0.0[/]")
            { Justification = Justify.Center });
        AnsiConsole.WriteLine();
    }

    public void ShowStatus(string message) =>
        AnsiConsole.MarkupLine($"[cyan]ℹ  {EscapeMarkup(message)}[/]");

    public void SetAppName(string name) => _appName = name;

    public async Task RunDownloadWithProgressAsync(DownloadEngine engine, DownloadSession session)
    {
        // Silence all log console output — TUI is the only renderer during download
        Logger.SilentMode = true;

        try
        {
            string header = string.IsNullOrWhiteSpace(_appName)
                ? $"AppID {session.AppId}"
                : _appName;

            AnsiConsole.WriteLine();

            await AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                    new TaskDescriptionColumn { Alignment = Justify.Left },
                    new ProgressBarColumn       { Width = 34 },
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn(Spinner.Known.Dots))
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask(
                        $"[cyan]{EscapeMarkup(header)}[/]",
                        maxValue: 100);

                    engine.ProgressChanged += (_, e) =>
                    {
                        task.Value = e.PercentComplete;

                        // Compose: "GameName  filename.ext  12.4 MB/s"
                        string fileName = e.CurrentFile is { Length: > 0 } f
                            ? Path.GetFileName(f)
                            : "...";
                        if (fileName.Length > 38)
                            fileName = "…" + fileName[^37..];

                        string speed = e.SpeedMBps >= 0.01
                            ? $"  [grey]{e.SpeedMBps:F1} MB/s[/]"
                            : "";

                        task.Description =
                            $"[cyan]{EscapeMarkup(header)}[/]  " +
                            $"[grey]{EscapeMarkup(fileName)}[/]{speed}";
                    };

                    await engine.RunAsync();

                    task.Value = 100;
                    task.StopTask();
                });

            AnsiConsole.WriteLine();

            if (session.WasCancelled)
            {
                AnsiConsole.MarkupLine("[yellow]⏸  Download paused. Resume with:[/]");
                AnsiConsole.MarkupLine(
                    $"[grey]   --app {session.AppId} --output \"{EscapeMarkup(session.OutputDir)}\"" +
                    $" --resume \"{EscapeMarkup(session.CheckpointPath)}\"[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[bold green]✓  Download complete![/]");
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C — checkpoint already saved by engine
        }
        finally
        {
            Logger.SilentMode = false;
        }
    }

    public void ShowSummary(DownloadStatistics stats)
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

    private static string EscapeMarkup(string s) =>
        s.Replace("[", "[[").Replace("]", "]]");
}
