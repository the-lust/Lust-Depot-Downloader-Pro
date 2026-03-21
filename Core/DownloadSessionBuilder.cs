using LustsDepotDownloaderPro.Models;
using LustsDepotDownloaderPro.Steam;
using LustsDepotDownloaderPro.Utils;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace LustsDepotDownloaderPro.Core;

public class DownloadSessionBuilder
{
    private readonly SteamSession    _steam;
    private readonly DownloadOptions _options;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public DownloadSessionBuilder(SteamSession steam, DownloadOptions options)
    {
        _steam   = steam;
        _options = options;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LustsDepotDownloaderPro/1.0");
    }

    // ─── App name ────────────────────────────────────────────────────────

    public async Task<string> GetAppNameAsync(uint appId)
    {
        try
        {
            var body = await _http.GetStringAsync(
                $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic");
            var name = JObject.Parse(body)[appId.ToString()]?["data"]?["name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        catch (Exception ex) { Logger.Debug($"GetAppName: {ex.Message}"); }
        return $"App_{appId}";
    }

    // ─── Workshop download ────────────────────────────────────────────────

    public async Task<DownloadSession> BuildWorkshopSessionAsync(string outputPath)
    {
        var session = new DownloadSession
        {
            AppId          = _options.AppId,
            AppName        = await GetAppNameAsync(_options.AppId),
            OutputDir      = outputPath,
            // SessionKey set by engine
            MaxDownloads   = _options.MaxDownloads,
            FallbackWorkers = _options.FallbackWorkers,
        };

        // Resolve workshop item → (depotId, manifestId) via SteamWorkshop
        try
        {
            if (_options.PubFileId.HasValue)
            {
                Logger.Warn($"Workshop downloads (PublishedFileId) are not yet fully implemented in this version.");
                Logger.Info("Workshop download requires complex Web API calls. Falling back to standard app download...");
                // TODO: Implement using ISteamRemoteStorage/GetPublishedFileDetails Web API
                // or SteamWorkshop handler
            }
            else if (_options.UgcId.HasValue)
            {
                Logger.Warn($"Workshop UGC downloads are not yet fully implemented in this version.");
                Logger.Info("Falling back to standard app download...");
                // TODO: Implement UGC download
            }

            // Fall back to building a normal session for the parent app
            return await BuildAsync(outputPath);
        }
        catch (Exception ex)
        {
            Logger.Error($"Workshop session build failed: {ex.Message}");
            throw;
        }
    }

    // ─── Main build ──────────────────────────────────────────────────────

    public async Task<DownloadSession> BuildAsync(string outputPath)
    {
        Logger.Debug($"Building session for AppID {_options.AppId}");

        var session = new DownloadSession
        {
            AppId             = _options.AppId,
            AppName           = await GetAppNameAsync(_options.AppId),
            OutputDir         = outputPath,
            // SessionKey is set by DownloadEngine after construction
            MaxDownloads      = _options.MaxDownloads,
            FallbackWorkers   = _options.FallbackWorkers,
            ValidateChecksums = _options.Validate
        };

        if (!string.IsNullOrEmpty(_options.FileListPath))
            session.FileFilters = FileUtils.LoadFileList(_options.FileListPath).ToHashSet();

        var userDepotKeys = LoadDepotKeys(_options.DepotKeysFile);

        // Community sources first (may save a CDN round-trip)
        Logger.Debug("Checking community manifest sources...");
        // Merge CLI --api-key with both baked-in tokens (PAT + Classic = 10k req/hr)
        var allTokens = new[] { _options.ApiKey }
            .Concat(EmbeddedConfig.GitHubTokens)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .Distinct()
            .ToArray();
        var fetcher = new ManifestSourceFetcher(allTokens);
        ManifestResult? communityResult = await fetcher.FetchAsync(_options.AppId);

        // Depot list from PICS
        List<uint> picsDepotIds;
        if (_options.DepotId.HasValue)
            picsDepotIds = new List<uint> { _options.DepotId.Value };
        else
            picsDepotIds = await GetDepotIdsForAppAsync(_options.AppId);

        // Merge PICS + community depot IDs
        var allDepotIds = new HashSet<uint>(picsDepotIds);
        if (communityResult != null)
        {
            int added = 0;
            foreach (var id in communityResult.Manifests.Keys)
                if (allDepotIds.Add(id)) added++;
            if (added > 0) Logger.Debug($"Added {added} community-only depot(s)");
        }

        if (allDepotIds.Count == 0)
            throw new Exception(
                "No depots found. Try --depot <id> --manifest <id> explicitly.");

        Logger.Debug($"Depots to process: {allDepotIds.Count}");

        // If manifest-only, just report and return empty session
        if (_options.ManifestOnly)
        {
            Logger.Info("=== MANIFEST-ONLY MODE ===");
            foreach (var depotId in allDepotIds)
            {
                ulong mid = 0;
                if (communityResult?.Manifests.TryGetValue(depotId, out var cm) == true)
                    mid = cm.ManifestId;
                if (mid == 0) mid = await GetManifestIdFromPicsAsync(_options.AppId, depotId) ?? 0;
                Logger.Info($"  Depot {depotId} → manifest {(mid == 0 ? "(not found)" : mid.ToString())}");
            }
            return session; // empty, no depots added
        }

        // Prepare each depot
        foreach (var depotId in allDepotIds)
        {
            var depot = await PrepareDepotAsync(
                _options.AppId, depotId, _options.ManifestId,
                userDepotKeys, communityResult);
            if (depot != null) session.Depots.Add(depot);
        }

        if (session.Depots.Count == 0)
            throw new Exception(
                $"No downloadable depots found from {allDepotIds.Count} tried. " +
                "For paid games, try --username/--password or ensure community sources have it.");

        Logger.Info($"Session ready: {session.Depots.Count}/{allDepotIds.Count} depot(s)");
        return session;
    }

    // ─── Single depot ─────────────────────────────────────────────────────

    private async Task<DepotInfo?> PrepareDepotAsync(
        uint appId, uint depotId, ulong? forcedManifestId,
        Dictionary<uint, byte[]> userDepotKeys, ManifestResult? communityResult)
    {
        try
        {
            Logger.Debug($"Preparing depot {depotId}");

            // 1. Depot key
            byte[]? depotKey = null;
            if (userDepotKeys.TryGetValue(depotId, out var uk))
            { depotKey = uk; Logger.Debug($"Depot {depotId}: user-provided key"); }
            else if (communityResult?.DepotKeys.TryGetValue(depotId, out var ck) == true)
            { depotKey = ck; Logger.Debug($"Depot {depotId}: community key"); }
            else
            {
                depotKey = await _steam.GetDepotKeyAsync(depotId, appId);
                if (depotKey == null)
                    Logger.Debug($"Depot {depotId}: no key (free/anon depot or access denied)");
            }

            // 2a. Local manifest file override
            if (!string.IsNullOrEmpty(_options.ManifestFile) && File.Exists(_options.ManifestFile))
            {
                var local = ManifestParser.LoadFromFile(_options.ManifestFile, depotKey);
                if (local != null)
                    return new DepotInfo
                    { DepotId = depotId, DepotName = $"Depot_{depotId}",
                      DepotKey = depotKey, ManifestId = 0, Files = local };
            }

            // 2b. Community binary manifest
            if (communityResult?.Manifests.TryGetValue(depotId, out var cm) == true && cm.Data != null)
            {
                Logger.Debug($"Depot {depotId}: using community manifest binary ({cm.Data.Length:N0} bytes)");
                try
                {
                    var dm = SteamKit2.DepotManifest.Deserialize(cm.Data);
                    if (depotKey != null) dm.DecryptFilenames(depotKey);
                    return new DepotInfo
                    { DepotId = depotId, DepotName = $"Depot_{depotId}",
                      DepotKey = depotKey, ManifestId = cm.ManifestId,
                      Files = ManifestParser.FromDepotManifest(dm) };
                }
                catch (Exception ex)
                { Logger.Warn($"Depot {depotId}: community manifest parse failed ({ex.Message}) — CDN fallback"); }
            }

            // 2c. Resolve manifest ID
            ulong manifestId = forcedManifestId ?? 0;
            if (manifestId == 0 && communityResult?.Manifests.TryGetValue(depotId, out var cme) == true)
                manifestId = cme.ManifestId;
            if (manifestId == 0)
                manifestId = await GetManifestIdFromPicsAsync(appId, depotId) ?? 0;

            if (manifestId == 0)
            {
                Logger.Warn($"Depot {depotId}: no manifest ID — skipping");
                return null;
            }

            // 2d. CDN manifest
            Logger.Debug($"Depot {depotId}: fetching manifest {manifestId} from CDN...");
            var manifest = await _steam.DownloadManifestAsync(
                appId, depotId, manifestId, depotKey,
                _options.Branch ?? "public", _options.BranchPassword);

            if (manifest == null)
            {
                Logger.Error($"Depot {depotId}: CDN manifest download failed. " +
                             "Log in with --username/--password for paid games.");
                return null;
            }

            return new DepotInfo
            { DepotId = depotId, DepotName = $"Depot_{depotId}",
              DepotKey = depotKey, ManifestId = manifestId,
              Files = ManifestParser.FromDepotManifest(manifest) };
        }
        catch (Exception ex)
        {
            Logger.Error($"Depot {depotId} failed: {ex.Message}");
            return null;
        }
    }

    // ─── PICS helpers ─────────────────────────────────────────────────────

    private async Task<ulong?> GetManifestIdFromPicsAsync(uint appId, uint depotId)
    {
        try
        {
            var result = await _steam.Apps.PICSGetProductInfo(
                new List<SteamApps.PICSRequest> { new SteamApps.PICSRequest(appId) },
                Enumerable.Empty<SteamApps.PICSRequest>());

            var apps = result.Results?.FirstOrDefault()?.Apps;
            if (apps == null || !apps.TryGetValue(appId, out var info)) return null;

            var depotKv = info.KeyValues["depots"][depotId.ToString()];
            if (depotKv == KeyValue.Invalid) return null;

            foreach (var branch in new[] { _options.Branch ?? "public", "public" }.Distinct())
            {
                var gid = depotKv["manifests"][branch]["gid"];
                if (gid != KeyValue.Invalid &&
                    !string.IsNullOrEmpty(gid.Value) &&
                    ulong.TryParse(gid.Value, out ulong mid))
                {
                    Logger.Info($"PICS depot {depotId} branch '{branch}' → manifest {mid}");
                    return mid;
                }
            }
        }
        catch (Exception ex) { Logger.Warn($"PICS manifest lookup depot {depotId}: {ex.Message}"); }
        return null;
    }

    private async Task<List<uint>> GetDepotIdsForAppAsync(uint appId)
    {
        var ids = new List<uint>();
        try
        {
            var result = await _steam.Apps.PICSGetProductInfo(
                new List<SteamApps.PICSRequest> { new SteamApps.PICSRequest(appId) },
                Enumerable.Empty<SteamApps.PICSRequest>());

            var apps = result.Results?.FirstOrDefault()?.Apps;
            if (apps == null || !apps.TryGetValue(appId, out var info)) return ids;

            foreach (var depot in info.KeyValues["depots"].Children)
            {
                if (!uint.TryParse(depot.Name, out uint depotId)) continue;

                if (!_options.AllPlatforms)
                {
                    var os = depot["config"]["oslist"].Value;
                    if (!string.IsNullOrEmpty(os) &&
                        !os.Contains(_options.Os, StringComparison.OrdinalIgnoreCase)) continue;
                }
                if (!_options.AllArchs)
                {
                    var arch = depot["config"]["osarch"].Value;
                    if (!string.IsNullOrEmpty(arch) && arch != _options.OsArch) continue;
                }
                if (!_options.AllLanguages)
                {
                    var lang = depot["config"]["language"].Value;
                    if (!string.IsNullOrEmpty(lang) &&
                        !lang.Equals(_options.Language, StringComparison.OrdinalIgnoreCase) &&
                        !lang.Equals("english", StringComparison.OrdinalIgnoreCase)) continue;
                }
                if (_options.LowViolence)
                {
                    var lv = depot["config"]["lowviolence"].Value;
                    if (string.IsNullOrEmpty(lv) || lv != "1") continue;
                }
                ids.Add(depotId);
            }
            Logger.Debug($"PICS: {ids.Count} depot(s) for app {appId}");
        }
        catch (Exception ex) { Logger.Warn($"GetDepotIds: {ex.Message}"); }
        return ids;
    }

    // ─── Depot key file ────────────────────────────────────────────────────

    private static Dictionary<uint, byte[]> LoadDepotKeys(string? path)
    {
        var keys = new Dictionary<uint, byte[]>();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return keys;
        try
        {
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var parts = line.Split(new[] { ';', '=', '\t', ' ' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !uint.TryParse(parts[0], out var id)) continue;
                keys[id] = Convert.FromHexString(parts[1].Replace(" ", "").Replace("-", ""));
            }
            Logger.Info($"Loaded {keys.Count} depot key(s) from file");
        }
        catch (Exception ex) { Logger.Warn($"LoadDepotKeys: {ex.Message}"); }
        return keys;
    }
}
