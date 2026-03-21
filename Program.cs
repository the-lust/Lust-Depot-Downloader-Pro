using System.CommandLine;
using System.CommandLine.Invocation;
using LustsDepotDownloaderPro.Core;
using LustsDepotDownloaderPro.Steam;
using LustsDepotDownloaderPro.UI;
using LustsDepotDownloaderPro.Utils;
using LustsDepotDownloaderPro.Models;
using Spectre.Console;

namespace LustsDepotDownloaderPro;

class Program
{
    private static DownloadSession? _currentSession;
    private static CancellationTokenSource? _cts;
    private static DownloadEngine? _engine;
    private static SteamSession? _steamSession;

    private static readonly Option<uint>    OptApp      = new(new[]{"--app","-a"},     ()=>0u,              "AppID to download");
    private static readonly Option<uint?>   OptDepot    = new(new[]{"--depot","-d"},                        "DepotID (all if omitted)");
    private static readonly Option<ulong?>  OptManifest = new(new[]{"--manifest","-m"},                     "Manifest ID");
    private static readonly Option<string>  OptBranch   = new(new[]{"--branch","-b"},  ()=>"public",        "Branch");
    private static readonly Option<string?> OptBranchPassword = new(new[]{"--branch-password","-bp"},       "Branch password");
    private static readonly Option<string?> OptUsername = new(new[]{"--username","-u"},                     "Steam username");
    private static readonly Option<string?> OptPassword = new(new[]{"--password","-p"},                     "Steam password");
    private static readonly Option<bool>    OptRememberPass   = new(new[]{"--remember-password","-rp"}, ()=>false, "Save credentials");
    private static readonly Option<bool>    OptQr             = new(new[]{"--qr"},              ()=>false,   "QR code auth");
    private static readonly Option<string?> OptDepotKeys      = new(new[]{"--depot-keys","-dk"},             "Depot keys file");
    private static readonly Option<string?> OptManifestFile   = new(new[]{"--manifest-file","-mf"},          "Local manifest file");
    private static readonly Option<string?> OptAppToken       = new(new[]{"--app-token","-at"},              "App access token");
    private static readonly Option<string?> OptPackageToken   = new(new[]{"--package-token","-pt"},          "Package access token");
    private static readonly Option<string>  OptOutput         = new(new[]{"--output","-o"}, ()=>Environment.CurrentDirectory, "Output directory");
    private static readonly Option<string?> OptFileList       = new(new[]{"--filelist","-fl"},               "File filter list");
    private static readonly Option<bool>    OptValidate       = new(new[]{"--validate","-v"}, ()=>false,     "Validate checksums");
    private static readonly Option<int>     OptMaxDownloads   = new(new[]{"--max-downloads","-md"}, ()=>8,   "Concurrent workers (1-64)");
    private static readonly Option<int?>    OptCellId         = new(new[]{"--cellid","-c"},                  "Override CDN cell ID");
    private static readonly Option<uint?>   OptLoginId        = new(new[]{"--loginid","-lid"},               "Steam LoginID");
    private static readonly Option<string>  OptOs             = new(new[]{"--os"},             GetCurrentOS, "OS (windows/macos/linux)");
    private static readonly Option<string>  OptOsArch         = new(new[]{"--os-arch","-arch"},GetCurrentArch,"Architecture (32/64)");
    private static readonly Option<string>  OptLanguage       = new(new[]{"--language","-lang"},()=>"english","Language");
    private static readonly Option<bool>    OptAllPlatforms   = new(new[]{"--all-platforms","-ap"},()=>false, "All platform depots");
    private static readonly Option<bool>    OptAllArchs       = new(new[]{"--all-archs","-aa"}, ()=>false,   "All arch depots");
    private static readonly Option<bool>    OptAllLanguages   = new(new[]{"--all-languages","-al"},()=>false,"All language depots");
    private static readonly Option<bool>    OptLowViolence    = new(new[]{"--low-violence","-lv"},()=>false, "Low-violence depots");
    private static readonly Option<ulong?>  OptPubFile        = new(new[]{"--pubfile","-pf"},                "PublishedFileId");
    private static readonly Option<ulong?>  OptUgc            = new(new[]{"--ugc"},                          "UGC ID");
    private static readonly Option<bool>    OptManifestOnly   = new(new[]{"--manifest-only","-mo"},()=>false,"Manifests only");
    private static readonly Option<bool>    OptDebug          = new(new[]{"--debug"},          ()=>false,   "Verbose debug logging");
    private static readonly Option<string?> OptApiKey         = new(new[]{"--api-key","-key"},              "GitHub API key");
    private static readonly Option<bool>    OptPause          = new(new[]{"--pause"},          ()=>false,   "Pause download");
    private static readonly Option<string?> OptResume         = new(new[]{"--resume","-r"},                 "Resume from checkpoint");
    private static readonly Option<bool>    OptStatus         = new(new[]{"--status","-s"},    ()=>false,   "Show status");
    private static readonly Option<bool>    OptTerminalUi     = new(new[]{"--terminal-ui","-tui"},()=>true, "Terminal UI");

    private static int _cancelHandled = 0;

    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "Lusts Depot Downloader Pro v1.0.0";

        var rootCommand = new RootCommand("Lusts Depot Downloader Pro — Steam Depot Downloader")
        {
            OptApp, OptDepot, OptManifest, OptBranch, OptBranchPassword,
            OptUsername, OptPassword, OptRememberPass, OptQr,
            OptDepotKeys, OptManifestFile, OptAppToken, OptPackageToken,
            OptOutput, OptFileList, OptValidate, OptMaxDownloads,
            OptCellId, OptLoginId, OptOs, OptOsArch, OptLanguage,
            OptAllPlatforms, OptAllArchs, OptAllLanguages, OptLowViolence,
            OptPubFile, OptUgc, OptManifestOnly, OptDebug, OptApiKey,
            OptPause, OptResume, OptStatus, OptTerminalUi
        };

        rootCommand.SetHandler(async ctx => await ExecuteDownloadAsync(ctx));
        Console.CancelKeyPress += OnCancelKeyPress;

        try { return await rootCommand.InvokeAsync(args); }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            return 1;
        }
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        if (Interlocked.CompareExchange(ref _cancelHandled, 1, 0) == 0)
        {
            AnsiConsole.MarkupLine("\n[yellow]⏸ Pausing — saving checkpoint...[/]");
            try
            {
                _cts?.Cancel();
                if (_currentSession != null)
                    CheckpointManager.SaveCheckpoint(_currentSession);
            }
            catch { }
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Force quitting...[/]");
            Environment.Exit(2);
        }
    }

    private static async Task<int> ExecuteDownloadAsync(InvocationContext ctx)
    {
        var r = ctx.ParseResult;

        var options = new DownloadOptions
        {
            AppId             = r.GetValueForOption(OptApp),
            DepotId           = r.GetValueForOption(OptDepot),
            ManifestId        = r.GetValueForOption(OptManifest),
            Branch            = r.GetValueForOption(OptBranch) ?? "public",
            BranchPassword    = r.GetValueForOption(OptBranchPassword),
            Username          = r.GetValueForOption(OptUsername),
            Password          = r.GetValueForOption(OptPassword),
            RememberPassword  = r.GetValueForOption(OptRememberPass),
            UseQr             = r.GetValueForOption(OptQr),
            DepotKeysFile     = r.GetValueForOption(OptDepotKeys),
            ManifestFile      = r.GetValueForOption(OptManifestFile),
            AppToken          = r.GetValueForOption(OptAppToken),
            PackageToken      = r.GetValueForOption(OptPackageToken),
            OutputDir         = r.GetValueForOption(OptOutput) ?? Environment.CurrentDirectory,
            FileListPath      = r.GetValueForOption(OptFileList),
            Validate          = r.GetValueForOption(OptValidate),
            MaxDownloads      = r.GetValueForOption(OptMaxDownloads),
            CellId            = r.GetValueForOption(OptCellId),
            LoginId           = r.GetValueForOption(OptLoginId),
            Os                = r.GetValueForOption(OptOs) ?? GetCurrentOS(),
            OsArch            = r.GetValueForOption(OptOsArch) ?? GetCurrentArch(),
            Language          = r.GetValueForOption(OptLanguage) ?? "english",
            AllPlatforms      = r.GetValueForOption(OptAllPlatforms),
            AllArchs          = r.GetValueForOption(OptAllArchs),
            AllLanguages      = r.GetValueForOption(OptAllLanguages),
            LowViolence       = r.GetValueForOption(OptLowViolence),
            PubFileId         = r.GetValueForOption(OptPubFile),
            UgcId             = r.GetValueForOption(OptUgc),
            ManifestOnly      = r.GetValueForOption(OptManifestOnly),
            Debug             = r.GetValueForOption(OptDebug),
            ApiKey            = r.GetValueForOption(OptApiKey),
            Pause             = r.GetValueForOption(OptPause),
            ResumeCheckpoint  = r.GetValueForOption(OptResume),
            ShowStatus        = r.GetValueForOption(OptStatus),
            TerminalUi        = r.GetValueForOption(OptTerminalUi)
        };

        if (options.Pause)  { HandlePause();  return 0; }
        if (options.ShowStatus) { ShowStatus(); return 0; }

        Logger.Initialize(options.Debug);

        // ── KEY: In non-debug mode, suppress all Info() from reaching console.
        //    Errors and Warnings still show. The TUI progress bar is the only
        //    output during an active download.
        Logger.QuietMode = !options.Debug;

        var ui = options.TerminalUi ? new TerminalUI() : null;
        ui?.ShowHeader();

        try
        {
            _cts = new CancellationTokenSource();

            var steamSession = new SteamSession();
            _steamSession = steamSession;

            // ── Auth ──────────────────────────────────────────────────────
            ui?.ShowStatus("Authenticating with Steam...");
            bool authenticated = await AuthenticateAsync(steamSession, options, ui);
            if (!authenticated)
            {
                AnsiConsole.MarkupLine("[red]❌ Authentication failed[/]");
                return 1;
            }
            ui?.ShowStatus("✓ Connected to Steam");

            // ── Session ───────────────────────────────────────────────────
            ui?.ShowStatus("Preparing download session...");
            _currentSession = await PrepareSessionAsync(steamSession, options, ui);
            if (_currentSession == null)
            {
                AnsiConsole.MarkupLine("[red]❌ Failed to prepare download session[/]");
                return 1;
            }

            int totalDepots = _currentSession.Depots.Count;
            long totalFiles = _currentSession.Depots.Sum(d => (long)d.Files.Count);
            ui?.ShowStatus(
                $"✓ Session ready — {_currentSession.AppName} · " +
                $"{totalDepots} depot(s) · {totalFiles:N0} files");

            // ── Download ──────────────────────────────────────────────────
            _engine = new DownloadEngine(_currentSession, steamSession, _cts.Token);

            if (ui != null)
            {
                ui.SetAppName(_currentSession.AppName);
                await ui.RunDownloadWithProgressAsync(_engine, _currentSession);
            }
            else
            {
                await _engine.RunAsync();
            }

            if (_cts.Token.IsCancellationRequested)
            {
                AnsiConsole.MarkupLine("[yellow]⏸ Download paused. Resume with --resume <checkpoint-file>[/]");
                return 2;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error($"Fatal: {ex.Message}");
            if (options.Debug) AnsiConsole.WriteException(ex);
            return 1;
        }
        finally { _cts?.Dispose(); }
    }

    private static async Task<bool> AuthenticateAsync(
        SteamSession session, DownloadOptions options, TerminalUI? ui)
    {
        if (string.IsNullOrEmpty(options.Username))
            return await session.ConnectAnonymousAsync();

        var creds = CredentialManager.Load(options.Username);
        options.Password ??= creds?.Password;

        if (string.IsNullOrEmpty(options.Password))
        {
            options.Password = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Steam password:[/]")
                    .PromptStyle("red").Secret());
        }

        bool ok = await session.ConnectAndLoginAsync(
            options.Username, options.Password, options.UseQr, options.RememberPassword);
        if (ok && options.RememberPassword)
            CredentialManager.Save(options.Username, options.Password);
        return ok;
    }

    private static async Task<DownloadSession?> PrepareSessionAsync(
        SteamSession steam, DownloadOptions options, TerminalUI? ui)
    {
        var builder  = new DownloadSessionBuilder(steam, options);
        string name  = await builder.GetAppNameAsync(options.AppId);
        string safe  = FileUtils.SanitizeFileName($"{options.AppId}_{name}");
        if (string.IsNullOrWhiteSpace(safe)) safe = options.AppId.ToString();
        string outputPath = Path.Combine(options.OutputDir, safe);

        var session = await builder.BuildAsync(outputPath);

        if (session != null && !string.IsNullOrEmpty(options.ResumeCheckpoint))
        {
            session.CheckpointPath = options.ResumeCheckpoint;
            Logger.Info($"Resume: using checkpoint {options.ResumeCheckpoint}");
        }

        return session;
    }

    private static void HandlePause()
    {
        if (_cts is { IsCancellationRequested: false })
            { _cts.Cancel(); AnsiConsole.MarkupLine("[yellow]⏸ Paused[/]"); }
        else
            AnsiConsole.MarkupLine("[yellow]ℹ No active download[/]");
    }

    private static void ShowStatus()
    {
        if (_engine == null || _currentSession == null)
            { AnsiConsole.MarkupLine("[yellow]ℹ No active session[/]"); return; }

        var s = _engine.GetStatistics();
        var t = new Table().Border(TableBorder.Rounded)
            .AddColumn("Metric").AddColumn("Value");
        t.AddRow("App ID",      _currentSession.AppId.ToString());
        t.AddRow("Downloaded",  $"{s.DownloadedMB:F2} MB / {s.TotalMB:F2} MB");
        t.AddRow("Progress",    $"{s.Percent:F2}%");
        t.AddRow("Speed",       $"{s.SpeedMBps:F2} MB/s");
        t.AddRow("Chunks",      $"{s.CompletedChunks} / {s.TotalChunks}");
        t.AddRow("Status",      s.IsCompleted ? "Completed" : s.IsPaused ? "Paused" : "Running");
        AnsiConsole.Write(t);
    }

    private static string GetCurrentOS()   => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
    private static string GetCurrentArch() => Environment.Is64BitOperatingSystem ? "64" : "32";
}
