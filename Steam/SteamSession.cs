using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.CDN;
using LustsDepotDownloaderPro.Utils;
using QRCoder;

namespace LustsDepotDownloaderPro.Steam;

public class SteamSession : IDisposable
{
    private readonly SteamClient   _steamClient;
    private readonly CallbackManager _callbackManager;
    private readonly SteamUser     _steamUser;
    private readonly SteamApps     _steamApps;
    private readonly SteamContent  _steamContent;

    private bool   _isRunning;
    private bool   _isLoggedIn;
    private bool   _isConnected;
    private string? _username;
    private string? _password;

    private readonly SemaphoreSlim _loginSignal   = new(0, 1);
    private readonly SemaphoreSlim _connectSignal = new(0, 1);

    // Optional config supplied by caller
    private int?  _cellId;
    private uint? _loginId;

    public SteamKit2.CDN.Client CdnClient { get; }
    public SteamApps Apps  => _steamApps;
    public SteamUser User  => _steamUser;
    public bool IsLoggedIn => _isLoggedIn;

    private IReadOnlyCollection<SteamKit2.CDN.Server>? _cdnServers;

    // CDN auth token cache — valid for multiple requests per (app, depot)
    private readonly Dictionary<(uint, uint), string> _cdnAuthTokens = new();

    public SteamSession(int? cellId = null, uint? loginId = null)
    {
        _cellId  = cellId;
        _loginId = loginId;

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

    // ─── Public connection API ─────────────────────────────────────────────

    public async Task<bool> ConnectAnonymousAsync()
    {
        Logger.Info("Connecting to Steam anonymously...");
        return await ConnectAndLoginInternalAsync(() => _steamUser.LogOnAnonymous());
    }

    public async Task<bool> ConnectAndLoginAsync(
        string username, string password,
        bool useQr = false, bool rememberPassword = false)
    {
        _username = username;
        _password = password;

        if (useQr)
            return await ConnectAndLoginViaQrAsync(username);

        return await ConnectAndLoginInternalAsync(() =>
            _steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username               = username,
                Password               = password,
                ShouldRememberPassword = rememberPassword,
                LoginID                = _loginId
            }));
    }

    // ─── QR code auth (NOT SUPPORTED IN SteamKit2 3.2.0) ─────────────────────

    private async Task<bool> ConnectAndLoginViaQrAsync(string username)
    {
        // QR code authentication is not available in SteamKit2 3.2.0
        // This feature requires SteamKit2 3.4.0 or higher
        Logger.Warn("❌ QR code authentication is not supported in this version.");
        Logger.Info("💡 Please use standard authentication instead:");
        Logger.Info("   --username <user> --password <pass>");
        Logger.Info("   Steam Guard and 2FA codes will be prompted as needed.");
        await Task.Delay(100); // Keep async signature
        return false;
    }

    // ─── Internal helpers ──────────────────────────────────────────────────

    private async Task<bool> EnsureConnectedAsync()
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            Logger.Debug($"Connection attempt {attempt}/{maxAttempts}...");
            _isRunning = true;
            _ = Task.Run(RunCallbackPump);

            _steamClient.Connect();

            bool connected = await _connectSignal.WaitAsync(TimeSpan.FromSeconds(30));
            if (connected && _isConnected)
            {
                Logger.Info("Connected to Steam");
                return true;
            }

            Logger.Warn($"Attempt {attempt} timed out — retrying...");
            _isRunning = false;
            _steamClient.Disconnect();
            await Task.Delay(2000);
        }
        Logger.Error("All Steam connection attempts failed");
        return false;
    }

    private async Task<bool> ConnectAndLoginInternalAsync(Action loginAction)
    {
        if (!await EnsureConnectedAsync()) return false;

        Logger.Info("Logging in...");
        loginAction();

        await _loginSignal.WaitAsync(TimeSpan.FromSeconds(45));
        if (_isLoggedIn)
        {
            Logger.Info("Successfully logged in");
            return true;
        }
        Logger.Error("Login failed");
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

    // ─── Depot key ────────────────────────────────────────────────────────

    public async Task<byte[]?> GetDepotKeyAsync(uint depotId, uint appId)
    {
        try
        {
            var cb = await _steamApps.GetDepotDecryptionKey(depotId, appId);
            if (cb.Result == EResult.OK) return cb.DepotKey;
            Logger.Warn($"GetDepotDecryptionKey depot {depotId}: {cb.Result}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"GetDepotKeyAsync: {ex.Message}");
            return null;
        }
    }

    // ─── CDN auth token ────────────────────────────────────────────────────

    /// <summary>
    /// In SteamKit2 3.x, GetCDNAuthToken lives on SteamApps.
    /// (It was on SteamContent in 2.x — moved back to SteamApps in 3.x.)
    /// </summary>
    private async Task<string?> GetCdnAuthTokenAsync(
        SteamKit2.CDN.Server server, uint appId, uint depotId)
    {
        var key = (appId, depotId);
        if (_cdnAuthTokens.TryGetValue(key, out var cached)) return cached;

        try
        {
            var hostName = server.Host ?? "";
            var cb = await _steamContent.GetCDNAuthToken(appId, depotId, hostName);
            if (cb.Result == EResult.OK)
            {
                _cdnAuthTokens[key] = cb.Token;
                return cb.Token;
            }
            Logger.Debug($"CDN auth token depot {depotId}: {cb.Result}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Debug($"GetCDNAuthToken: {ex.Message}");
            return null;
        }
    }

    // ─── Manifest request code ─────────────────────────────────────────────

    public async Task<ulong> GetManifestRequestCodeAsync(
        uint appId, uint depotId, ulong manifestId,
        string branch = "public", string? branchPassword = null)
    {
        try
        {
            return await _steamContent.GetManifestRequestCode(
                appId, depotId, manifestId, branch, branchPassword);
        }
        catch (Exception ex)
        {
            Logger.Debug($"GetManifestRequestCode: {ex.Message} — using 0");
            return 0;
        }
    }

    // ─── CDN servers ──────────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<SteamKit2.CDN.Server>> GetCdnServersAsync()
    {
        if (_cdnServers != null) return _cdnServers;
        try
        {
            _cdnServers = await _steamContent.GetServersForSteamPipe();
            Logger.Debug($"CDN servers available: {_cdnServers.Count}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to fetch CDN servers: {ex.Message}");
            _cdnServers = Array.Empty<SteamKit2.CDN.Server>();
        }
        return _cdnServers;
    }

    // ─── Manifest download ────────────────────────────────────────────────

    public async Task<SteamKit2.DepotManifest?> DownloadManifestAsync(
        uint appId, uint depotId, ulong manifestId, byte[]? depotKey,
        string branch = "public", string? branchPassword = null)
    {
        ulong requestCode = await GetManifestRequestCodeAsync(
            appId, depotId, manifestId, branch, branchPassword);

        var servers = await GetCdnServersAsync();
        Exception? lastEx = null;

        foreach (var server in servers.Take(8))
        {
            try
            {
                var manifest = await CdnClient.DownloadManifestAsync(
                    depotId, manifestId, requestCode, server, depotKey,
                    proxyServer: null, cdnAuthToken: null);
                Logger.Debug($"Manifest downloaded ({manifest.Files?.Count ?? 0} files)");
                return manifest;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Logger.Debug($"401 on {server.Host} — requesting CDN auth token...");
                var token = await GetCdnAuthTokenAsync(server, appId, depotId);
                if (token == null) { lastEx = ex; continue; }
                try
                {
                    var manifest = await CdnClient.DownloadManifestAsync(
                        depotId, manifestId, requestCode, server, depotKey,
                        proxyServer: null, cdnAuthToken: token);
                    Logger.Debug($"Manifest downloaded with auth token ({manifest.Files?.Count ?? 0} files)");
                    return manifest;
                }
                catch (Exception ex2) { lastEx = ex2; Logger.Debug($"CDN retry {server.Host}: {ex2.Message}"); }
            }
            catch (Exception ex) { lastEx = ex; Logger.Debug($"{server.Host}: {ex.Message}"); }
        }

        Logger.Error($"All CDN servers failed for manifest {manifestId}: {lastEx?.Message}");
        return null;
    }

    // ─── Steam callbacks ───────────────────────────────────────────────────

    private void OnConnected(SteamClient.ConnectedCallback _)
    {
        Logger.Info($"Connected: {_steamClient.CurrentEndPoint}");
        _isConnected = true;
        try { _connectSignal.Release(); } catch { }
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback cb)
    {
        Logger.Info($"Disconnected (user-initiated: {cb.UserInitiated})");
        _isConnected = false;
        _isLoggedIn  = false;
        try { _connectSignal.Release(); } catch { }
        try { _loginSignal.Release(); }   catch { }
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback cb)
    {
        if (cb.Result == EResult.AccountLogonDenied)
        {
            Console.Write("\nSteam Guard code (email): ");
            var code = Console.ReadLine();
            _steamUser.LogOn(new SteamUser.LogOnDetails
                { Username = _username, Password = _password, AuthCode = code,
                  LoginID = _loginId });
            return;
        }

        if (cb.Result == EResult.AccountLoginDeniedNeedTwoFactor)
        {
            Console.Write("\nSteam Mobile Authenticator code: ");
            var code = Console.ReadLine();
            _steamUser.LogOn(new SteamUser.LogOnDetails
                { Username = _username, Password = _password, TwoFactorCode = code,
                  LoginID = _loginId });
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

    // ─── IDisposable ──────────────────────────────────────────────────────

    public void Dispose()
    {
        _isRunning = false;
        try { if (_isLoggedIn) _steamUser.LogOff(); } catch { }
        try { _steamClient.Disconnect(); } catch { }
        _loginSignal.Dispose();
        _connectSignal.Dispose();
    }
}
