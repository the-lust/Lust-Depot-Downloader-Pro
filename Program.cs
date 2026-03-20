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
    private static int _cancelHandled = 0;

    // ── Options ────────────────────────────────────────────────────────────────
    private static readonly Option<uint>    OptApp       = new(new[]{"--app","-a"},   ()=>0u,   "AppID");
    private static readonly Option<uint?>   OptDepot     = new(new[]{"--depot","-d"},           "DepotID");
    private static readonly Option<ulong?>  OptManifest  = new(new[]{"--manifest","-m"},        "Manifest ID");
    private static readonly Option<string>  OptBranch    = new(new[]{"--branch","-b"}, ()=>"public", "Branch");
    private static readonly Option<string?> OptBranchPw  = new(new[]{"--branch-password","-bp"}, "Branch password");
    private static readonly Option<string?> OptUsername  = new(new[]{"--username","-u"},        "Steam username");
    private static readonly Option<string?> OptPassword  = new(new[]{"--password","-p"},        "Steam password");
    private static readonly Option<bool>    OptRemember  = new(new[]{"--remember-password","-rp"}, ()=>false, "Save credentials");
    private static readonly Option<bool>    OptQr        = new(new[]{"--qr"}, ()=>false,         "QR code login");
    private static readonly Option<string?> OptDepotKeys = new(new[]{"--depot-keys","-dk"},     "Depot keys file");
    private static readonly Option<string?> OptManifestFile = new(new[]{"--manifest-file","-mf"}, "Local manifest file");
    private static readonly Option<string?> OptAppToken  = new(new[]{"--app-token","-at"},      "App access token");
    private static readonly Option<string?> OptPkgToken  = new(new[]{"--package-token","-pt"},  "Package access token");
    private static readonly Option<string>  OptOutput    = new(new[]{"--output","-o"}, ()=>Environment.CurrentDirectory, "Output directory");
    private static readonly Option<string?> OptFileList  = new(new[]{"--filelist","-fl"},       "File filter list");
    private static readonly Option<bool>    OptValidate  = new(new[]{"--validate","-v"}, ()=>false, "Verify checksums");
    private static readonly Option<int>     OptMaxDl     = new(new[]{"--max-downloads","-md"}, ()=>8, "Workers (1-64)");
    private static readonly Option<int?>    OptCellId    = new(new[]{"--cellid","-c"},          "CDN cell ID override");
    private static readonly Option<uint?>   OptLoginId   = new(new[]{"--loginid","-lid"},       "Steam LoginID");
    private static readonly Option<string>  OptOs        = new(new[]{"--os"}, GetCurrentOS,     "OS (windows/macos/linux)");
    private static readonly Option<string>  OptOsArch    = new(new[]{"--os-arch","-arch"}, GetCurrentArch, "Arch (32/64)");
    private static readonly Option<string>  OptLanguage  = new(new[]{"--language","-lang"}, ()=>"english", "Language");
    private static readonly Option<bool>    OptAllPlat   = new(new[]{"--all-platforms","-ap"}, ()=>false, "All platform depots");
    private static readonly Option<bool>    OptAllArchs  = new(new[]{"--all-archs","-aa"}, ()=>false, "All arch depots");
    private static readonly Option<bool>    OptAllLang   = new(new[]{"--all-languages","-al"}, ()=>false, "All language depots");
    private static readonly Option<bool>    OptLowV      = new(new[]{"--low-violence","-lv"}, ()=>false, "Low-violence depots");
    private static readonly Option<ulong?>  OptPubFile   = new(new[]{"--pubfile","-pf"},        "Workshop PublishedFileId");
    private static readonly Option<ulong?>  OptUgc       = new(new[]{"--ugc"},                  "Workshop UGC ID");
    private static readonly Option<bool>    OptManOnly   = new(new[]{"--manifest-only","-mo"}, ()=>false, "List manifests only");
    private static readonly Option<bool>    OptDebug     = new(new[]{"--debug"},  ()=>false,    "Verbose logging");
    private static readonly Option<string?> OptApiKey    = new(new[]{"--api-key","-key"},       "GitHub API key");
    private static readonly Option<bool>    OptPause     = new(new[]{"--pause"},  ()=>false,    "Pause active download");
    private static readonly Option<string?> OptResume    = new(new[]{"--resume","-r"},          "Resume from checkpoint");
    private static readonly Option<bool>    OptStatus    = new(new[]{"--status","-s"}, ()=>false, "Show status");
    private static readonly Option<bool>    OptTui       = new(new[]{"--terminal-ui","-tui"}, ()=>true, "Terminal UI");

    // ── Entry point ────────────────────────────────────────────────────────────

    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title          = "Lusts Depot Downloader Pro v1.0.0";

        var root = new RootCommand("Lusts Depot Downloader Pro — Steam Depot Downloader")
        {
            OptApp, OptDepot, OptManifest, OptBranch, OptBranchPw,
            OptUsername, OptPassword, OptRemember, OptQr,
            OptDepotKeys, OptManifestFile, OptAppToken, OptPkgToken,
            OptOutput, OptFileList, OptValidate, OptMaxDl,
            OptCellId, OptLoginId, OptOs, OptOsArch, OptLanguage,
            OptAllPlat, OptAllArchs, OptAllLang, OptLowV,
            OptPubFile, OptUgc, OptManOnly, OptDebug, OptApiKey,
            OptPause, OptResume, OptStatus, OptTui
        };

        root.SetHandler(async ctx => await RunAsync(ctx));
        Console.CancelKeyPress += OnCancelKeyPress;

        try { return await root.InvokeAsync(args); }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            return 1;
        }
    }

    // ── Ctrl+C ─────────────────────────────────────────────────────────────────
    //
    // First press  → cancel CTS.  DownloadEngine.RunAsync() catches the
    //                cancellation, calls _checkpoint.Save(), and returns.
    //                Do NOT write the session here — that overwrites the
    //                checkpoint file with full session JSON, breaking resume.
    // Second press → hard exit.
    //
    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        if (Interlocked.CompareExchange(ref _cancelHandled, 1, 0) == 0)
        {
            AnsiConsole.MarkupLine("\n[yellow]⏸  Pausing download...[/]");
            AnsiConsole.MarkupLine("[dim]Checkpoint will be saved. Press Ctrl+C again to force quit.[/]");
            _cts?.Cancel();
            // Engine.RunAsync catches cancellation and saves checkpoint
        }
        else
        {
            AnsiConsole.MarkupLine("[red]⚠  Force quitting (checkpoint may not be saved)...[/]");
            Environment.Exit(2);
        }
    }

    // ── Main handler ────────────────────────────────────────────────────────────

    private static async Task<int> RunAsync(InvocationContext ctx)
    {
        var r = ctx.ParseResult;

        var options = new DownloadOptions
        {
            AppId            = r.GetValueForOption(OptApp),
            DepotId          = r.GetValueForOption(OptDepot),
            ManifestId       = r.GetValueForOption(OptManifest),
            Branch           = r.GetValueForOption(OptBranch) ?? "public",
            BranchPassword   = r.GetValueForOption(OptBranchPw),
            Username         = r.GetValueForOption(OptUsername),
            Password         = r.GetValueForOption(OptPassword),
            RememberPassword = r.GetValueForOption(OptRemember),
            UseQr            = r.GetValueForOption(OptQr),
            DepotKeysFile    = r.GetValueForOption(OptDepotKeys),
            ManifestFile     = r.GetValueForOption(OptManifestFile),
            AppToken         = r.GetValueForOption(OptAppToken),
            PackageToken     = r.GetValueForOption(OptPkgToken),
            OutputDir        = r.GetValueForOption(OptOutput) ?? Environment.CurrentDirectory,
            FileListPath     = r.GetValueForOption(OptFileList),
            Validate         = r.GetValueForOption(OptValidate),
            MaxDownloads     = Math.Clamp(r.GetValueForOption(OptMaxDl), 1, 64),
            CellId           = r.GetValueForOption(OptCellId),
            LoginId          = r.GetValueForOption(OptLoginId),
            Os               = r.GetValueForOption(OptOs) ?? GetCurrentOS(),
            OsArch           = r.GetValueForOption(OptOsArch) ?? GetCurrentArch(),
            Language         = r.GetValueForOption(OptLanguage) ?? "english",
            AllPlatforms     = r.GetValueForOption(OptAllPlat),
            AllArchs         = r.GetValueForOption(OptAllArchs),
            AllLanguages     = r.GetValueForOption(OptAllLang),
            LowViolence      = r.GetValueForOption(OptLowV),
            PubFileId        = r.GetValueForOption(OptPubFile),
            UgcId            = r.GetValueForOption(OptUgc),
            ManifestOnly     = r.GetValueForOption(OptManOnly),
            Debug            = r.GetValueForOption(OptDebug),
            ApiKey           = r.GetValueForOption(OptApiKey),
            Pause            = r.GetValueForOption(OptPause),
            ResumeCheckpoint = r.GetValueForOption(OptResume),
            ShowStatus       = r.GetValueForOption(OptStatus),
            TerminalUi       = r.GetValueForOption(OptTui),
        };

        if (options.Pause)   { AnsiConsole.MarkupLine("[yellow]⏸  No active process to pause[/]"); return 0; }
        if (options.ShowStatus) { AnsiConsole.MarkupLine("[yellow]ℹ  No active download session[/]"); return 0; }

        Logger.Initialize(options.Debug);
        Logger.Info("Lusts Depot Downloader Pro v1.0.0");

        // Steam session — pass cellid and loginid
        var steam = new SteamSession(options.CellId, options.LoginId);
        _steamSession = steam;

        TerminalUI? ui = options.TerminalUi ? new TerminalUI() : null;
        ui?.ShowHeader();

        try
        {
            _cts = new CancellationTokenSource();

            // Auth
            bool authed = await AuthenticateAsync(steam, options, ui);
            if (!authed)
            {
                AnsiConsole.MarkupLine("[red]❌ Authentication failed[/]");
                return 1;
            }

            // Session
            _currentSession = await BuildSessionAsync(steam, options, ui);
            if (_currentSession == null)
            {
                AnsiConsole.MarkupLine("[red]❌ Failed to prepare download session[/]");
                return 1;
            }

            // Manifest-only: session is built with empty Depots list
            if (options.ManifestOnly)
            {
                AnsiConsole.MarkupLine("[green]✓ Manifest listing complete[/]");
                return 0;
            }

            _engine = new DownloadEngine(_currentSession, steam, _cts.Token);

            if (ui != null)
            {
                await ui.RunDownloadWithProgressAsync(_engine);
            }
            else
            {
                // GUI / plain mode: subscribe to progress and write structured output
                // that the Electron UI's stdout parser can read
                AttachPlainProgressReporter(_engine);
                await _engine.RunAsync();
            }

            if (_cts.Token.IsCancellationRequested)
            {
                AnsiConsole.MarkupLine("[yellow]⏸  Download paused successfully![/]");
                AnsiConsole.MarkupLine($"[dim]Checkpoint: {_currentSession.CheckpointPath}[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[cyan]To resume, run:[/]");
                AnsiConsole.MarkupLine($"  [white]--app {options.AppId} --output \"{options.OutputDir}\" " +
                                       $"--resume \"{_currentSession.CheckpointPath}\"[/]");
                return 2;
            }

            AnsiConsole.MarkupLine("[green]✓ Download completed successfully![/]");
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error($"Fatal error: {ex.Message}");
            if (options.Debug) AnsiConsole.WriteException(ex);
            return 1;
        }
        finally
        {
            _cts?.Dispose();
            steam.Dispose();
            Logger.Dispose();
        }
    }

    // ── Plain progress reporter (--terminal-ui false) ──────────────────────────
    //
    // Writes lines the Electron UI's parseLDDPLine() can parse:
    //   Downloading <file>  ━━━━━━━━━  45% 00:05:30
    //   Speed: 3.14 MB/s
    //
    private static void AttachPlainProgressReporter(DownloadEngine engine)
    {
        const int BLOCKS = 20;
        DateTime _lastReport = DateTime.MinValue;

        engine.ProgressChanged += (_, e) =>
        {
            // Throttle to 1 update per second
            var now = DateTime.UtcNow;
            if ((now - _lastReport).TotalMilliseconds < 1000) return;
            _lastReport = now;

            string file   = e.CurrentFile ?? "...";
            int    n      = (int)Math.Round(e.PercentComplete / 100.0 * BLOCKS);
            string filled = new string('━', Math.Max(0, n));
            string empty  = new string('━', Math.Max(0, BLOCKS - n));
            string eta    = FormatEta(e.EtaSeconds);
            int    pct    = (int)Math.Round(e.PercentComplete);

            // Write directly to stdout without Logger timestamp prefix
            // so the UI's ^Downloading regex matches from start of line
            Console.WriteLine($"Downloading {file}  {filled}{empty}  {pct}% {eta}");
            Console.WriteLine($"Speed: {e.SpeedMBps:F2} MB/s");
        };
    }

    private static string FormatEta(double etaSec)
    {
        if (etaSec <= 0) return "--:--:--";
        var t = TimeSpan.FromSeconds(etaSec);
        return $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    private static async Task<bool> AuthenticateAsync(
        SteamSession steam, DownloadOptions options, TerminalUI? ui)
    {
        ui?.ShowStatus("Authenticating with Steam...");

        if (string.IsNullOrEmpty(options.Username))
            return await steam.ConnectAnonymousAsync();

        var creds = CredentialManager.Load(options.Username);
        options.Password ??= creds?.Password;

        if (string.IsNullOrEmpty(options.Password) && !options.UseQr)
        {
            options.Password = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Steam password:[/]")
                    .PromptStyle("red").Secret());
        }

        bool ok = await steam.ConnectAndLoginAsync(
            options.Username, options.Password ?? "",
            options.UseQr, options.RememberPassword);

        if (ok && options.RememberPassword && !string.IsNullOrEmpty(options.Password))
            CredentialManager.Save(options.Username, options.Password);

        return ok;
    }

    // ── Session builder ────────────────────────────────────────────────────────

    private static async Task<DownloadSession?> BuildSessionAsync(
        SteamSession steam, DownloadOptions options, TerminalUI? ui)
    {
        ui?.ShowStatus("Preparing download session...");

        var builder = new DownloadSessionBuilder(steam, options);

        string name = await builder.GetAppNameAsync(options.AppId);
        string safe = FileUtils.SanitizeFileName($"{options.AppId}_{name}");
        if (string.IsNullOrWhiteSpace(safe)) safe = options.AppId.ToString();

        string outputPath = Path.Combine(options.OutputDir, safe);
        ui?.ShowStatus($"Output: {outputPath}");

        DownloadSession? session;

        // Workshop item?
        if (options.PubFileId.HasValue || options.UgcId.HasValue)
            session = await builder.BuildWorkshopSessionAsync(outputPath);
        else
            session = await builder.BuildAsync(outputPath);

        if (session == null) return null;

        // Attach resume checkpoint — DO NOT call LoadFromCheckpoint (wrong format).
        // Just set CheckpointPath; DownloadEngine.Checkpoint.Load() reads the file.
        if (!string.IsNullOrEmpty(options.ResumeCheckpoint))
        {
            session.CheckpointPath = options.ResumeCheckpoint;
            Logger.Info($"Resuming from checkpoint: {options.ResumeCheckpoint}");
        }

        return session;
    }

    private static string GetCurrentOS()    => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
    private static string GetCurrentArch()  => Environment.Is64BitOperatingSystem ? "64" : "32";
}
