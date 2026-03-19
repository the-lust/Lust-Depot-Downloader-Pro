using LustsDepotDownloaderPro.Models;
using LustsDepotDownloaderPro.Steam;
using LustsDepotDownloaderPro.Utils;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace LustsDepotDownloaderPro.Core;

public class DownloadSessionBuilder
{
    private readonly SteamSession _steam;
    private readonly DownloadOptions _options;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public DownloadSessionBuilder(SteamSession steam, DownloadOptions options)
    {
        _steam   = steam;
        _options = options;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LustsDepotDownloaderPro/1.0");
    }

    // ─── App name ─────────────────────────────────────────────────────────────

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

    // ─── Main build ───────────────────────────────────────────────────────────

    public async Task<DownloadSession> BuildAsync(string outputPath)
    {
        Logger.Info($"Building session for AppID {_options.AppId}");

        var session = new DownloadSession
        {
            AppId             = _options.AppId,
            AppName           = await GetAppNameAsync(_options.AppId),
            OutputDir         = outputPath,
            CheckpointPath    = Path.Combine(outputPath, $"checkpoint_{_options.AppId}.json"),
            MaxDownloads      = _options.MaxDownloads,
            ValidateChecksums = _options.Validate
        };

        if (!string.IsNullOrEmpty(_options.FileListPath))
            session.FileFilters = FileUtils.LoadFileList(_options.FileListPath).ToHashSet();

        var userDepotKeys = LoadDepotKeys(_options.DepotKeysFile);

        // ── Community manifests (always try first — avoids 401 for anon sessions) ──
        Logger.Info("Fetching manifests from community sources...");
        var fetcher         = new ManifestSourceFetcher(_options.ApiKey);
        ManifestResult? communityResult = await fetcher.FetchAsync(_options.AppId);

        // ── Depot list from PICS ──────────────────────────────────────────────
        List<uint> picsDepotIds;
        if (_options.DepotId.HasValue)
            picsDepotIds = new List<uint> { _options.DepotId.Value };
        else
            picsDepotIds = await GetDepotIdsForAppAsync(_options.AppId);

        // FIX: ALWAYS merge community depot IDs with PICS depot IDs.
        // The original code only used community IDs when PICS returned 0, meaning
        // any depot in a community source but not in PICS (e.g. because the game
        // filters it out by OS/arch/lang) was silently skipped.
        // Now: start with PICS list, then ADD any community-only depots on top.
        var allDepotIds = new HashSet<uint>(picsDepotIds);
        if (communityResult != null)
        {
            int before = allDepotIds.Count;
            foreach (var id in communityResult.Manifests.Keys)
                allDepotIds.Add(id);
            int added = allDepotIds.Count - before;
            if (added > 0)
                Logger.Info($"Added {added} community-only depot(s) not returned by PICS");
        }

        if (allDepotIds.Count == 0)
            throw new Exception(
                "Could not determine depot list. " +
                "Try --depot <id> --manifest <id> --depot-keys keys.txt explicitly.");

        Logger.Info($"Total depots to attempt: {allDepotIds.Count} " +
                    $"(PICS: {picsDepotIds.Count}, " +
                    $"community: {communityResult?.Manifests.Count ?? 0})");

        // ── Prepare each depot ────────────────────────────────────────────────
        int prepared = 0;
        foreach (var depotId in allDepotIds)
        {
            var depot = await PrepareDepotAsync(
                _options.AppId, depotId, _options.ManifestId,
                userDepotKeys, communityResult);
            if (depot != null)
            {
                session.Depots.Add(depot);
                prepared++;
            }
        }

        if (session.Depots.Count == 0)
            throw new Exception(
                $"No downloadable depots found out of {allDepotIds.Count} tried. " +
                "If this is a paid game: community sources may not have it yet, " +
                "or try --username / --password to log in with your Steam account.");

        Logger.Info($"Session ready: {session.Depots.Count}/{allDepotIds.Count} depot(s) prepared");
        return session;
    }

    // ─── Single depot preparation ─────────────────────────────────────────────

    private async Task<DepotInfo?> PrepareDepotAsync(
        uint appId, uint depotId, ulong? forcedManifestId,
        Dictionary<uint, byte[]> userDepotKeys,
        ManifestResult? communityResult)
    {
        try
        {
            Logger.Info($"Preparing depot {depotId}");

            // ── 1. Depot key: user file > community source > Steam API ────────
            byte[]? depotKey = null;

            if (userDepotKeys.TryGetValue(depotId, out var provided))
            {
                depotKey = provided;
                Logger.Debug($"Depot {depotId}: using user-provided key");
            }
            else if (communityResult?.DepotKeys.TryGetValue(depotId, out var ck) == true)
            {
                depotKey = ck;
                Logger.Debug($"Depot {depotId}: using community key");
            }
            else
            {
                depotKey = await _steam.GetDepotKeyAsync(depotId, appId);
                if (depotKey == null)
                    Logger.Debug($"Depot {depotId}: no key available " +
                                 "(free/unencrypted depot, or anonymous access denied)");
            }

            // ── 2. Manifest ───────────────────────────────────────────────────

            // 2a. Local manifest file override
            if (!string.IsNullOrEmpty(_options.ManifestFile) &&
                File.Exists(_options.ManifestFile))
            {
                Logger.Info($"Using local manifest file: {_options.ManifestFile}");
                var local = ManifestParser.LoadFromFile(_options.ManifestFile, depotKey);
                if (local != null)
                    return new DepotInfo
                    {
                        DepotId    = depotId, DepotName = $"Depot_{depotId}",
                        DepotKey   = depotKey, ManifestId = 0, Files = local
                    };
            }

            // 2b. Community binary manifest — parse directly (no CDN needed)
            if (communityResult?.Manifests.TryGetValue(depotId, out var cm) == true
                && cm.Data != null)
            {
                Logger.Info($"Depot {depotId}: parsing community manifest " +
                            $"({cm.Data.Length:N0} bytes)");
                try
                {
                    var dm = SteamKit2.DepotManifest.Deserialize(cm.Data);
                    if (depotKey != null) dm.DecryptFilenames(depotKey);
                    return new DepotInfo
                    {
                        DepotId    = depotId,
                        DepotName  = $"Depot_{depotId}",
                        DepotKey   = depotKey,
                        ManifestId = cm.ManifestId,
                        Files      = ManifestParser.FromDepotManifest(dm)
                    };
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Depot {depotId}: community manifest parse failed " +
                                $"({ex.Message}), falling back to CDN");
                }
            }

            // 2c. Resolve manifest ID: explicit flag > community ID > PICS
            ulong manifestId = forcedManifestId ?? 0;

            if (manifestId == 0 &&
                communityResult?.Manifests.TryGetValue(depotId, out var cme) == true)
                manifestId = cme.ManifestId;

            if (manifestId == 0)
                manifestId = await GetManifestIdFromPicsAsync(appId, depotId) ?? 0;

            if (manifestId == 0)
            {
                Logger.Warn($"Depot {depotId}: no manifest ID found — skipping. " +
                            "(Community sources don't have this depot yet.)");
                return null;
            }

            // 2d. CDN manifest download (requires ownership for paid games)
            Logger.Info($"Depot {depotId}: downloading manifest {manifestId} from CDN...");
            var manifest = await _steam.DownloadManifestAsync(
                appId, depotId, manifestId, depotKey,
                _options.Branch ?? "public", _options.BranchPassword);

            if (manifest == null)
            {
                Logger.Error(
                    $"Depot {depotId}: CDN manifest download failed. " +
                    "For paid games, community sources must have the manifest binary, " +
                    "OR use --username / --password to log in with your Steam account.");
                return null;
            }

            return new DepotInfo
            {
                DepotId    = depotId,
                DepotName  = $"Depot_{depotId}",
                DepotKey   = depotKey,
                ManifestId = manifestId,
                Files      = ManifestParser.FromDepotManifest(manifest)
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"Depot {depotId} failed: {ex.Message}");
            return null;
        }
    }

    // ─── PICS ─────────────────────────────────────────────────────────────────

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
        catch (Exception ex)
        {
            Logger.Warn($"PICS manifest lookup depot {depotId}: {ex.Message}");
        }
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
                        !os.Contains(_options.Os, StringComparison.OrdinalIgnoreCase))
                        continue;
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
                ids.Add(depotId);
            }
            Logger.Info($"PICS: {ids.Count} depot(s) for app {appId}");
        }
        catch (Exception ex) { Logger.Warn($"GetDepotIds: {ex.Message}"); }
        return ids;
    }

    // ─── Depot key file loader ─────────────────────────────────────────────────

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
            Logger.Info($"Loaded {keys.Count} depot key(s)");
        }
        catch (Exception ex) { Logger.Warn($"LoadDepotKeys: {ex.Message}"); }
        return keys;
    }
}
