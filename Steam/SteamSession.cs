using SteamKit2;
using SteamKit2.CDN;
using LustsDepotDownloaderPro.Utils;

namespace LustsDepotDownloaderPro.Steam;

public class SteamSession : IDisposable
{
    private readonly SteamClient _steamClient;
    private readonly CallbackManager _callbackManager;
    private readonly SteamUser _steamUser;
    private readonly SteamApps _steamApps;
    private readonly SteamContent _steamContent;

    private bool _isRunning;
    private bool _isLoggedIn;
    private bool _isConnected;
    private string? _username;
    private string? _password;

    private readonly SemaphoreSlim _loginSignal   = new(0, 1);
    private readonly SemaphoreSlim _connectSignal = new(0, 1);

    public SteamKit2.CDN.Client CdnClient { get; }
    public SteamApps Apps    => _steamApps;
    public SteamUser User    => _steamUser;
    public bool IsLoggedIn   => _isLoggedIn;

    private IReadOnlyCollection<SteamKit2.CDN.Server>? _cdnServers;

    // Cache CDN auth tokens per (appId, depotId) to avoid re-requesting them
    private readonly Dictionary<(uint appId, uint depotId), string> _cdnAuthTokens = new();

    public SteamSession()
    {
        var config = SteamConfiguration.Create(c =>
            c.WithWebAPIKey("")
             .WithConnectionTimeout(TimeSpan.FromSeconds(30)));

        _steamClient     = new SteamClient(config);
        _callbackManager = new CallbackManager(_steamClient);
        _steamUser       = _steamClient.GetHandler<SteamUser>()!;
        _steamApps       = _steamClient.GetHandler<SteamApps>()!;
        _steamContent    = _steamClient.GetHandler<SteamContent>()!;
        CdnClient        = new SteamKit2.CDN.Client(_steamClient);

        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
    }

    // ─── Connection ───────────────────────────────────────────────────────────

    public async Task<bool> ConnectAnonymousAsync()
    {
        Logger.Info("Connecting to Steam anonymously...");
        return await ConnectAndLoginAsync(() => _steamUser.LogOnAnonymous());
    }

    public async Task<bool> ConnectAndLoginAsync(
        string username, string password,
        bool useQr = false, bool rememberPassword = false)
    {
        _username = username;
        _password = password;
        return await ConnectAndLoginAsync(() =>
            _steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username               = username,
                Password               = password,
                ShouldRememberPassword = rememberPassword
            }));
    }

    private async Task<bool> ConnectAndLoginAsync(Action loginAction)
    {
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            Logger.Info($"Steam connection attempt {attempt}/{maxAttempts}...");
            _isRunning = true;
            _ = Task.Run(RunCallbackPump);

            _steamClient.Connect();

            bool connected = await _connectSignal.WaitAsync(TimeSpan.FromSeconds(30));
            if (!connected || !_isConnected)
            {
                Logger.Warn($"Attempt {attempt} — connection timed out, retrying...");
                _isRunning = false;
                _steamClient.Disconnect();
                await Task.Delay(2000);
                continue;
            }

            Logger.Info("Connected — logging in...");
            loginAction();

            bool loggedIn = await _loginSignal.WaitAsync(TimeSpan.FromSeconds(45));
            if (_isLoggedIn)
            {
                Logger.Info("Successfully logged in to Steam");
                return true;
            }

            Logger.Warn($"Attempt {attempt} — login failed, retrying...");
            _isRunning = false;
            _steamClient.Disconnect();
            await Task.Delay(2000);
        }

        Logger.Error("All Steam connection attempts failed");
        return false;
    }

    private void RunCallbackPump()
    {
        while (_isRunning)
        {
            try { _callbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(1)); }
            catch (Exception ex) { Logger.Debug($"Callback pump: {ex.Message}"); }
        }
    }

    // ─── Depot key ────────────────────────────────────────────────────────────

    public async Task<byte[]?> GetDepotKeyAsync(uint depotId, uint appId)
    {
        try
        {
            var cb = await _steamApps.GetDepotDecryptionKey(depotId, appId);
            if (cb.Result != EResult.OK)
            {
                Logger.Warn($"GetDepotDecryptionKey depot {depotId}: {cb.Result}");
                return null;
            }
            return cb.DepotKey;
        }
        catch (Exception ex)
        {
            Logger.Warn($"GetDepotKeyAsync: {ex.Message}");
            return null;
        }
    }

    // ─── CDN auth token (needed for anonymous manifest downloads) ─────────────

    /// <summary>
    /// Requests a CDN auth token via SteamApps.GetCDNAuthToken, which is the
    /// correct API in SteamKit2 2.4.0.  SteamContent does NOT expose
    /// GetCDNAuthToken in this version — that method lives on SteamApps.
    /// </summary>
    private async Task<string?> GetCdnAuthTokenAsync(
        SteamKit2.CDN.Server server, uint appId, uint depotId)
    {
        // Cache per (appId, depotId) — token is valid for many requests
        var key = (appId, depotId);
        if (_cdnAuthTokens.TryGetValue(key, out var cached))
            return cached;

        try
        {
            // FIX (SteamKit2 3.x): VHost was removed; Host is the only hostname field
            var hostName = server.Host ?? "";
            var cb = await _steamContent.GetCDNAuthToken(appId, depotId, hostName);

            if (cb.Result == EResult.OK)
            {
                Logger.Debug($"CDN auth token for depot {depotId}: OK");
                _cdnAuthTokens[key] = cb.Token;
                return cb.Token;
            }

            Logger.Debug($"CDN auth token depot {depotId}: {cb.Result} — trying without token");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Debug($"GetCDNAuthToken: {ex.Message}");
            return null;
        }
    }

    // ─── Manifest request code ────────────────────────────────────────────────

    public async Task<ulong> GetManifestRequestCodeAsync(
        uint appId, uint depotId, ulong manifestId,
        string branch = "public", string? branchPassword = null)
    {
        try
        {
            ulong code = await _steamContent.GetManifestRequestCode(
                appId, depotId, manifestId, branch, branchPassword);
            Logger.Debug($"Manifest request code depot {depotId}/{manifestId}: {code}");
            return code;
        }
        catch (Exception ex)
        {
            Logger.Debug($"GetManifestRequestCode: {ex.Message} — using 0");
            return 0;
        }
    }

    // ─── CDN servers ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<SteamKit2.CDN.Server>> GetCdnServersAsync()
    {
        if (_cdnServers != null) return _cdnServers;
        try
        {
            _cdnServers = await _steamContent.GetServersForSteamPipe();
            Logger.Info($"CDN servers: {_cdnServers.Count}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to fetch CDN servers: {ex.Message}");
            _cdnServers = Array.Empty<SteamKit2.CDN.Server>();
        }
        return _cdnServers;
    }

    // ─── Manifest download ────────────────────────────────────────────────────

    /// <summary>
    /// Download a manifest, trying both with and without CDN auth token.
    /// Anonymous sessions require a CDNAuthToken per server; authenticated
    /// sessions use a manifest request code.
    /// </summary>
    public async Task<SteamKit2.DepotManifest?> DownloadManifestAsync(
        uint appId, uint depotId, ulong manifestId, byte[]? depotKey,
        string branch = "public", string? branchPassword = null)
    {
        // Try manifest request code first (works for authenticated sessions)
        ulong requestCode = await GetManifestRequestCodeAsync(
            appId, depotId, manifestId, branch, branchPassword);

        var servers = await GetCdnServersAsync();
        Exception? lastEx = null;

        foreach (var server in servers.Take(8))
        {
            try
            {
                Logger.Debug($"Trying manifest {manifestId} from {server.Host}");
                // FIX (SteamKit2 3.x): new optional params proxyServer + cdnAuthToken added
                var manifest = await CdnClient.DownloadManifestAsync(
                    depotId, manifestId, requestCode, server, depotKey,
                    proxyServer: null, cdnAuthToken: null);
                Logger.Info($"Manifest downloaded — {manifest.Files?.Count ?? 0} files");
                return manifest;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("401"))
            {
                // FIX: The original code fetched a CDN auth token but then called
                // DownloadManifestAsync with the same unmodified `server` object —
                // the token was never actually used.
                //
                // SteamKit2 CDN.Client builds its request URL as:
                //   /depot/{depotId}/manifest/{manifestId}/5/{requestCode}
                // and appends ?token=TOKEN when the Server.VHost or Token field is set.
                //
                // Since SteamKit2 CDN.Server is a sealed record we can't mutate its Token
                // property directly; instead we build a new Server with the token set via
                // the copy constructor pattern. If the version of SteamKit2 in use does
                // not expose a Token property, the manifest download itself should still
                // work when requestCode > 0 (authenticated session), or the game must be
                // in community sources (which don't need CDN at all).
                Logger.Debug($"401 on {server.Host}, requesting CDN auth token...");
                var token = await GetCdnAuthTokenAsync(server, appId, depotId);
                if (token == null)
                {
                    lastEx = ex;
                    continue;
                }

                try
                {
                    // FIX (SteamKit2 3.x): CDN auth token is now passed as a string
                    // directly to DownloadManifestAsync via the cdnAuthToken parameter.
                    // The old approach of re-using requestCode was a workaround that
                    // didn't actually work anyway.
                    var manifest = await CdnClient.DownloadManifestAsync(
                        depotId, manifestId, requestCode, server, depotKey,
                        proxyServer: null, cdnAuthToken: token);
                    Logger.Info($"Manifest downloaded with CDN auth token — {manifest.Files?.Count ?? 0} files");
                    return manifest;
                }
                catch (Exception ex2)
                {
                    lastEx = ex2;
                    Logger.Debug($"CDN retry failed on {server.Host}: {ex2.Message}");
                }
            }
            catch (Exception ex)
            {
                lastEx = ex;
                Logger.Debug($"Server {server.Host}: {ex.Message}");
            }
        }

        Logger.Error($"All CDN servers failed for manifest {manifestId}: {lastEx?.Message}");
        return null;
    }

    // ─── Callbacks ────────────────────────────────────────────────────────────

    private void OnConnected(SteamClient.ConnectedCallback _)
    {
        Logger.Info($"Connected to Steam CM: {_steamClient.CurrentEndPoint}");
        _isConnected = true;
        try { _connectSignal.Release(); } catch { }
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback cb)
    {
        Logger.Info($"Disconnected (user-initiated: {cb.UserInitiated})");
        _isConnected = false;
        _isLoggedIn  = false;
        try { _connectSignal.Release(); } catch { }
        try { _loginSignal.Release(); } catch { }
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback cb)
    {
        if (cb.Result == EResult.AccountLogonDenied)
        {
            Console.Write("\nEnter Steam Guard e-mail code: ");
            var code = Console.ReadLine();
            _steamUser.LogOn(new SteamUser.LogOnDetails
                { Username = _username, Password = _password, AuthCode = code });
            return;
        }

        if (cb.Result == EResult.AccountLoginDeniedNeedTwoFactor)
        {
            Console.Write("\nEnter Steam Mobile Authenticator code: ");
            var code = Console.ReadLine();
            _steamUser.LogOn(new SteamUser.LogOnDetails
                { Username = _username, Password = _password, TwoFactorCode = code });
            return;
        }

        if (cb.Result != EResult.OK)
        {
            Logger.Error($"Login failed: {cb.Result} / {cb.ExtendedResult}");
            _isLoggedIn = false;
        }
        else
        {
            _isLoggedIn = true;
            Logger.Info($"Logged in — SteamID: {cb.ClientSteamID}");
        }
        try { _loginSignal.Release(); } catch { }
    }

    private void OnLoggedOff(SteamUser.LoggedOffCallback cb)
    {
        Logger.Warn($"Logged off: {cb.Result}");
        _isLoggedIn = false;
    }

    // ─── IDisposable ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        _isRunning = false;
        try { if (_isLoggedIn) _steamUser.LogOff(); } catch { }
        try { _steamClient.Disconnect(); } catch { }
        _loginSignal.Dispose();
        _connectSignal.Dispose();
    }
}