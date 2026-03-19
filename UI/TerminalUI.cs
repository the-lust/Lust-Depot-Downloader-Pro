using Spectre.Console;
using LustsDepotDownloaderPro.Core;

namespace LustsDepotDownloaderPro.UI;

public class TerminalUI
{
    private ProgressTask? _mainProgress;
    private ProgressTask? _speedProgress;

    public void ShowHeader()
    {
        var rule = new Rule("[bold cyan]Lusts Depot Downloader Pro[/]")
        {
            Justification = Justify.Center
        };
        
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();
        
        var panel = new Panel(
            "[bold]Features:[/]\n" +
            "  ✓ Multi-threaded downloading\n" +
            "  ✓ Pause/Resume support\n" +
            "  ✓ CDN failover\n" +
            "  ✓ Manifest & key support\n" +
            "  ✓ Workshop items\n" +
            "  ✓ Progress tracking")
        {
            Header = new PanelHeader("[bold yellow]⚡ Welcome[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan1)
        };
        
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    public void ShowStatus(string message)
    {
        AnsiConsole.MarkupLine($"[cyan]ℹ️  {message}[/]");
    }

    public async Task RunDownloadWithProgressAsync(DownloadEngine engine)
    {
        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                _mainProgress = ctx.AddTask("[cyan]Downloading[/]", maxValue: 100);
                _speedProgress = ctx.AddTask("[yellow]Speed[/]", maxValue: 100);

                // Subscribe to progress events
                engine.ProgressChanged += (s, e) =>
                {
                    if (_mainProgress != null)
                    {
                        _mainProgress.Value = e.PercentComplete;
                        _mainProgress.Description = $"[cyan]Downloading {e.CurrentFile ?? "..."}[/]";
                    }

                    if (_speedProgress != null)
                    {
                        _speedProgress.Description = $"[yellow]Speed: {e.SpeedMBps:F2} MB/s[/]";
                    }
                };

                // Run download
                await engine.RunAsync();

                if (_mainProgress != null)
                {
                    _mainProgress.Value = 100;
                    _mainProgress.StopTask();
                }

                if (_speedProgress != null)
                {
                    _speedProgress.StopTask();
                }
            });
    }

    public void ShowSummary(Models.DownloadStatistics stats)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .AddColumn(new TableColumn("[bold]Metric[/]").Centered())
            .AddColumn(new TableColumn("[bold]Value[/]").Centered());

        table.AddRow("Downloaded", $"[green]{stats.DownloadedMB:F2} MB[/]");
        table.AddRow("Total", $"{stats.TotalMB:F2} MB");
        table.AddRow("Progress", $"[cyan]{stats.Percent:F2}%[/]");
        table.AddRow("Speed", $"[yellow]{stats.SpeedMBps:F2} MB/s[/]");
        table.AddRow("Chunks", $"{stats.CompletedChunks} / {stats.TotalChunks}");
        table.AddRow("Status", stats.IsCompleted ? "[green]✓ Completed[/]" : stats.IsPaused ? "[yellow]⏸️  Paused[/]" : "[cyan]Running[/]");

        AnsiConsole.Write(table);
    }

    public string PromptForInput(string message, string? defaultValue = null)
    {
        if (defaultValue != null)
        {
            return AnsiConsole.Prompt(
                new TextPrompt<string>($"[cyan]{message}[/]")
                    .DefaultValue(defaultValue));
        }
        else
        {
            return AnsiConsole.Prompt(
                new TextPrompt<string>($"[cyan]{message}[/]"));
        }
    }

    public bool PromptForConfirmation(string message)
    {
        return AnsiConsole.Confirm($"[yellow]{message}[/]");
    }

    public T PromptForChoice<T>(string message, params T[] choices) where T : notnull
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<T>()
                .Title($"[cyan]{message}[/]")
                .AddChoices(choices));
    }
}
