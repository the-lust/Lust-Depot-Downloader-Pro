using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LustsDepotDownloaderPro.Utils;
using Newtonsoft.Json.Linq;

namespace LustsDepotDownloaderPro.Steam;

/// <summary>
/// Fetches manifests and depot keys from every known community source.
/// All sources run in parallel; results are merged — a game whose keys live
/// in repo #5 and whose manifest binary is in repo #18 will get both.
///
/// ══════════════════════════════════════════════════════════════════
///  GitHub branch-type sources (per-AppID branch, Key.vdf + .manifest)
/// ══════════════════════════════════════════════════════════════════
///   1.  ikun0014/ManifestHub
///   2.  Auiowu/ManifestAutoUpdate
///   3.  tymolu233/ManifestAutoUpdate
///   4.  SteamAutoCracks/ManifestHub
///   5.  sean-who/ManifestAutoUpdate          (XOR-encrypted Key.vdf)
///   6.  BlankTMing/ManifestAutoUpdate         ★ original by the BlankTMing
///   7.  wxy1343/ManifestAutoUpdate
///   8.  pjy612/SteamManifestCache            ★ 1.5k stars — manifests only
///   9.  nicklvsa/ManifestAutoUpdate
///  10.  P-ToyStore/SteamManifestCache_Pro
///  11.  isKoi/ManifestAutoUpdate
///  12.  yunxiao6/ManifestAutoUpdate
///  13.  BlueAmulet/ManifestAutoUpdate
///  14.  nicholasess/ManifestAutoUpdate
///  15.  masqueraigne/ManifestAutoUpdate
///  16.  WoodenTiger000/SteamManifestHub
///  17.  TheSecondComing001/SteamManifestHub
///  18.  eudaimence/OpenDepot
///  19.  ikunshare/ManifestHub
///  20.  Onekey-Project/Manifest-AutoUpdate
///  21.  SteamManifestHub/ManifestHub           (archive mirror)
///  22.  forcesteam/ManifestAutoUpdate
///  23.  Egsagon/ManifestAutoUpdate
///  24.  itsnotlupus/ManifestAutoUpdate
///  25.  zxcv3000/ManifestAutoUpdate
///  26.  r0ck3tz/ManifestAutoUpdate
///  27.  AlexIsTheGuy/ManifestAutoUpdate
///  28.  SteamContentLeak/ManifestAutoUpdate
///  29.  Kiraio-lgtm/ManifestAutoUpdate
///  30.  DreamSourceLab/ManifestAutoUpdate
///
/// ══════════════════════════════════════════════════════════════════
///  Special encrypted source
/// ══════════════════════════════════════════════════════════════════
///  31.  luckygametools/steam-cfg              (AES+XOR+gob .dat)
///
/// ══════════════════════════════════════════════════════════════════
///  REST / ZIP sources
/// ══════════════════════════════════════════════════════════════════
///  32.  printedwaste.com
///  33.  steambox.gdata.fun
///  34.  cysaw.top
/// </summary>
public class ManifestSourceFetcher
{
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        AllowAutoRedirect = true
    })
    { Timeout = TimeSpan.FromSeconds(45) };

    private readonly string? _githubToken;

    // XOR key used by sean-who/ManifestAutoUpdate for Key.vdf
    private static readonly byte[] SeanWhoXorKey =
        Encoding.UTF8.GetBytes("Scalping dogs, I'll fuck you");

    // AES key used by luckygametools
    private static readonly byte[] LuckyAesKey =
        Encoding.UTF8.GetBytes(" s  t  e  a  m  ");

    // XOR key used by luckygametools after AES
    private static readonly byte[] LuckyXorKey = Encoding.UTF8.GetBytes("hail");

    // Thread-safe rate-limit flag
    private volatile int _githubRateLimited = 0;

    // ─── Source table ─────────────────────────────────────────────────────
    // (repo, xorKeyForKeyVdf)   null = plain Key.vdf, no extra encryption

    private static readonly (string Repo, byte[]? XorKey)[] GitHubSources =
    {
        // ── Tier 1: original / highest-coverage repos ─────────────────────
        ("ikun0014/ManifestHub",                null),
        ("Auiowu/ManifestAutoUpdate",           null),
        ("tymolu233/ManifestAutoUpdate",        null),
        ("SteamAutoCracks/ManifestHub",         null),
        ("sean-who/ManifestAutoUpdate",         SeanWhoXorKey),

        // ── Tier 2: high-star, actively maintained ───────────────────────
        ("BlankTMing/ManifestAutoUpdate",       null),   // ★405 — the OG
        ("wxy1343/ManifestAutoUpdate",          null),   // ★ referenced by oureveryday tools
        ("pjy612/SteamManifestCache",           null),   // ★1.5k — manifests only (no keys)

        // ── Tier 3: broad community forks / mirrors ──────────────────────
        ("nicklvsa/ManifestAutoUpdate",         null),
        ("P-ToyStore/SteamManifestCache_Pro",   null),
        ("isKoi/ManifestAutoUpdate",            null),
        ("yunxiao6/ManifestAutoUpdate",         null),
        ("BlueAmulet/ManifestAutoUpdate",       null),
        ("nicholasess/ManifestAutoUpdate",      null),
        ("masqueraigne/ManifestAutoUpdate",     null),
        ("WoodenTiger000/SteamManifestHub",     null),   // mirror of SteamAutoCracks
        ("TheSecondComing001/SteamManifestHub", null),   // mirror of SteamAutoCracks
        ("eudaimence/OpenDepot",                null),
        ("ikunshare/ManifestHub",               null),   // ikun's own hub
        ("Onekey-Project/Manifest-AutoUpdate",  null),   // referenced by Onekey forks

        // ── Tier 4: additional community repos ──────────────────────────
        ("forcesteam/ManifestAutoUpdate",       null),
        ("Egsagon/ManifestAutoUpdate",          null),
        ("itsnotlupus/ManifestAutoUpdate",      null),
        ("zxcv3000/ManifestAutoUpdate",         null),
        ("r0ck3tz/ManifestAutoUpdate",          null),
        ("AlexIsTheGuy/ManifestAutoUpdate",     null),
        ("SteamContentLeak/ManifestAutoUpdate", null),
        ("Kiraio-lgtm/ManifestAutoUpdate",      null),
        ("DreamSourceLab/ManifestAutoUpdate",   null),
    };

    public ManifestSourceFetcher(string? githubToken = null)
    {
        _githubToken = githubToken;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LustsDepotDownloaderPro/1.0");
    }

    // ─── Public API ───────────────────────────────────────────────────────

    /// <summary>
    /// Fetch from ALL 34 sources in parallel and merge results.
    /// </summary>
    public async Task<ManifestResult?> FetchAsync(uint appId)
    {
        Logger.Info($"Searching ALL community manifest sources for app {appId}...");
        _githubRateLimited = 0;

        var merged = new ManifestResult { AppId = appId, Source = "merged" };

        // All GitHub repos + luckygametools + REST sources — fully parallel
        var githubTasks = GitHubSources
            .Select(s => TryGitHubBranchAsync(appId, s.Repo, s.XorKey))
            .ToList();

        var luckyTask = TryLuckyGameToolsAsync(appId);

        var restTasks = new List<Task<ManifestResult?>>
        {
            TryPrintedWasteAsync(appId),
            TryGdataAsync(appId),
            TryCysawAsync(appId),
        };

        var allTasks = githubTasks
            .Concat(new[] { luckyTask })
            .Concat(restTasks);

        ManifestResult?[] results;
        try
        {
            results = await Task.WhenAll(allTasks);
        }
        catch
        {
            results = allTasks.Select(t => t.IsCompletedSuccessfully ? t.Result : null).ToArray();
        }

        int newManifests = 0, newKeys = 0;
        foreach (var r in results)
        {
            if (r == null || (r.Manifests.Count == 0 && r.DepotKeys.Count == 0)) continue;
            int mBefore = merged.Manifests.Count, kBefore = merged.DepotKeys.Count;
            MergeInto(merged, r);
            int mAdded = merged.Manifests.Count - mBefore;
            int kAdded = merged.DepotKeys.Count - kBefore;
            newManifests += mAdded; newKeys += kAdded;
            if (mAdded > 0 || kAdded > 0)
                Logger.Info($"[{r.Source}] +{mAdded} manifest(s), +{kAdded} key(s)");
        }

        if (_githubRateLimited == 1)
            Logger.Warn("GitHub API rate limit hit. Use --api-key <github_pat> " +
                        "to raise cap from 60 to 5000 req/hr.");

        if (merged.Manifests.Count == 0 && merged.DepotKeys.Count == 0)
        {
            Logger.Warn($"No community data found for app {appId}. " +
                        "The game may not be in any community repo yet, " +
                        "or GitHub rate limit was reached (use --api-key).");
            return null;
        }

        Logger.Info($"Total from all sources: {merged.Manifests.Count} depot(s), " +
                    $"{merged.DepotKeys.Count} key(s)");
        return merged;
    }

    // ─── Merge helper ─────────────────────────────────────────────────────

    private static void MergeInto(ManifestResult target, ManifestResult source)
    {
        foreach (var (id, entry) in source.Manifests)
        {
            if (!target.Manifests.TryGetValue(id, out var existing))
                target.Manifests[id] = entry;
            else if (existing.Data == null && entry.Data != null)
                target.Manifests[id] = entry;   // upgrade ID-only → binary
        }
        foreach (var (id, key) in source.DepotKeys)
            if (!target.DepotKeys.ContainsKey(id))
                target.DepotKeys[id] = key;
    }

    // ─── GitHub branch source ─────────────────────────────────────────────

    private async Task<ManifestResult?> TryGitHubBranchAsync(
        uint appId, string repo, byte[]? xorDecryptKey)
    {
        try
        {
            Logger.Debug($"[{repo}] checking branch {appId}...");
            var headers = BuildGitHubHeaders();

            var branchUrl  = $"https://api.github.com/repos/{repo}/branches/{appId}";
            var branchJson = await FetchJsonAsync(branchUrl, headers);
            if (branchJson == null || !branchJson.ContainsKey("commit")) return null;

            string sha     = branchJson["commit"]!["sha"]!.ToString();
            string treeUrl = branchJson["commit"]!["commit"]!["tree"]!["url"]!.ToString();

            var treeJson = await FetchJsonAsync(treeUrl, headers);
            if (treeJson == null || !treeJson.ContainsKey("tree")) return null;

            var tree   = treeJson["tree"]!.ToArray();
            var result = new ManifestResult { AppId = appId, Source = repo };

            // Download all .manifest binaries in parallel within this repo
            var manifestDownloads = tree
                .Where(i => i["path"]!.ToString().EndsWith(".manifest"))
                .Select(async item =>
                {
                    string path  = item["path"]!.ToString();
                    var parts    = Path.GetFileNameWithoutExtension(path).Split('_');
                    if (parts.Length < 2)                                 return;
                    if (!uint.TryParse(parts[0],  out uint  depotId))    return;
                    if (!ulong.TryParse(parts[1], out ulong manifestId)) return;

                    byte[]? data = await FetchRawAsync(sha, path, repo);
                    if (data == null) return;

                    lock (result)
                    {
                        result.Manifests[depotId] = new ManifestEntry
                        {
                            DepotId    = depotId,
                            ManifestId = manifestId,
                            Data       = data
                        };
                    }
                    Logger.Info($"[{repo}] manifest {depotId}_{manifestId} ✓");
                });

            await Task.WhenAll(manifestDownloads);

            // Depot keys from Key.vdf (or config.vdf for some repos)
            foreach (var vdfName in new[] { "key.vdf", "Key.vdf", "config.vdf" })
            {
                var keyEntry = tree.FirstOrDefault(i =>
                    string.Equals(i["path"]!.ToString(), vdfName,
                        StringComparison.OrdinalIgnoreCase));
                if (keyEntry == null) continue;

                byte[]? keyData = await FetchRawAsync(sha, keyEntry["path"]!.ToString(), repo);
                if (keyData != null)
                {
                    ParseKeyVdf(keyData, result, xorDecryptKey);
                    break; // found one, stop
                }
            }

            return result.Manifests.Count > 0 || result.DepotKeys.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            Logger.Debug($"[{repo}] {ex.Message}");
            return null;
        }
    }

    // ─── luckygametools/steam-cfg ─────────────────────────────────────────

    private async Task<ManifestResult?> TryLuckyGameToolsAsync(uint appId)
    {
        const string repo = "luckygametools/steam-cfg";
        try
        {
            Logger.Debug($"[{repo}] trying...");
            var headers = BuildGitHubHeaders();

            var contentsUrl  = $"https://api.github.com/repos/{repo}/contents/steamdb2/{appId}";
            var contentsJson = await FetchJsonArrayAsync(contentsUrl, headers);
            if (contentsJson == null) return null;

            string? datPath = contentsJson
                .FirstOrDefault(i => i["name"]?.ToString() == "00000encrypt.dat")
                ?["path"]?.ToString();
            if (datPath == null) return null;

            byte[]? raw = await FetchRawAsync("main", datPath, repo);
            if (raw == null) return null;

            byte[]? aesDecrypted = SymmetricDecrypt(LuckyAesKey, raw);
            if (aesDecrypted == null) return null;
            byte[] xorDecrypted = XorDecrypt(LuckyXorKey, aesDecrypted);

            var result = await ParseGobViaPythonAsync(appId, xorDecrypted)
                      ?? ParseGobViaVzScanner(appId, xorDecrypted);

            if (result != null && (result.Manifests.Count > 0 || result.DepotKeys.Count > 0))
            {
                result.Source = repo;
                return result;
            }
        }
        catch (Exception ex) { Logger.Debug($"[{repo}] {ex.Message}"); }
        return null;
    }

    private static async Task<ManifestResult?> ParseGobViaPythonAsync(
        uint appId, byte[] gobData)
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "Scripts", "parse_luckygob.py"),
            Path.Combine(AppContext.BaseDirectory, "parse_luckygob.py"),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "parse_luckygob.py"),
        };
        string? script = candidates.FirstOrDefault(File.Exists);
        if (script == null)
        {
            Logger.Debug("parse_luckygob.py not found — falling back to VZ scanner");
            return null;
        }

        try
        {
            string tmpIn  = Path.GetTempFileName();
            string tmpOut = Path.GetTempFileName();
            await File.WriteAllBytesAsync(tmpIn, gobData);

            foreach (string pythonExe in new[] { "python", "python3" })
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName               = pythonExe,
                        Arguments              = $"\"{script}\" \"{tmpIn}\" \"{tmpOut}\"",
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                    };
                    using var proc = Process.Start(psi)!;
                    await proc.WaitForExitAsync();

                    if (proc.ExitCode == 0 && File.Exists(tmpOut))
                    {
                        string json = await File.ReadAllTextAsync(tmpOut);
                        File.Delete(tmpIn); File.Delete(tmpOut);
                        return ParseGobJson(appId, json);
                    }
                    string err = await proc.StandardError.ReadToEndAsync();
                    Logger.Debug($"parse_luckygob.py [{pythonExe}] exit {proc.ExitCode}: {err.Trim()}");
                }
                catch { }
            }
            File.Delete(tmpIn); File.Delete(tmpOut);
        }
        catch (Exception ex) { Logger.Debug($"ParseGobViaPythonAsync: {ex.Message}"); }
        return null;
    }

    private static ManifestResult? ParseGobJson(uint appId, string json)
    {
        try
        {
            var doc    = JsonDocument.Parse(json);
            var result = new ManifestResult { AppId = appId };

            foreach (var depot in doc.RootElement.GetProperty("depots").EnumerateArray())
            {
                uint  depotId    = depot.GetProperty("id").GetUInt32();
                ulong manifestId = depot.GetProperty("manifestId").GetUInt64();

                if (depot.TryGetProperty("decryptKey", out var keyElem))
                {
                    string hex = keyElem.GetString() ?? "";
                    if (hex.Length == 64)
                        result.DepotKeys[depotId] = Convert.FromHexString(hex);
                }

                byte[]? data = null;
                if (depot.TryGetProperty("manifestData", out var dataElem))
                {
                    string b64 = dataElem.GetString() ?? "";
                    if (!string.IsNullOrEmpty(b64)) data = Convert.FromBase64String(b64);
                }

                result.Manifests[depotId] = new ManifestEntry
                    { DepotId = depotId, ManifestId = manifestId, Data = data };
            }
            return result;
        }
        catch (Exception ex) { Logger.Debug($"ParseGobJson: {ex.Message}"); return null; }
    }

    private static ManifestResult? ParseGobViaVzScanner(uint appId, byte[] data)
    {
        Logger.Debug($"[luckygametools] VZ scanner: scanning {data.Length} bytes...");
        int found = 0;

        for (int i = 0; i < data.Length - 4; i++)
        {
            if (data[i] != 0x56 || data[i + 1] != 0x5A) continue;
            int[] trySizes = { 2 * 1024 * 1024, 1024 * 1024, 512 * 1024, 256 * 1024 };
            foreach (int sz in trySizes)
            {
                int end = Math.Min(i + sz, data.Length);
                try
                {
                    var dm = SteamKit2.DepotManifest.Deserialize(data[i..end]);
                    if (dm?.Files == null || dm.Files.Count == 0) continue;
                    found++;
                    Logger.Debug($"[luckygametools] VZ hit @{i}: {dm.Files.Count} files");
                    break;
                }
                catch { }
            }
        }

        if (found == 0) Logger.Debug("[luckygametools] VZ scanner: no valid manifests found");
        return null; // depot IDs unavailable without full gob parsing
    }

    // ─── printedwaste.com ────────────────────────────────────────────────

    private async Task<ManifestResult?> TryPrintedWasteAsync(uint appId)
    {
        const string source = "printedwaste.com";
        try
        {
            Logger.Debug($"[{source}] trying...");
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.printedwaste.com/gfk/download/{appId}");
            req.Headers.TryAddWithoutValidation("Authorization",
                "Bearer dGhpc19pcyBhX3JhbmRvbV90b2tlbg==");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            return await ParseZipSourceAsync(appId, source,
                await resp.Content.ReadAsByteArrayAsync());
        }
        catch (Exception ex) { Logger.Debug($"[{source}] {ex.Message}"); return null; }
    }

    // ─── steambox.gdata.fun ──────────────────────────────────────────────

    private async Task<ManifestResult?> TryGdataAsync(uint appId)
    {
        const string source = "steambox.gdata.fun";
        try
        {
            Logger.Debug($"[{source}] trying...");
            var resp = await _http.GetAsync(
                $"https://steambox.gdata.fun/cnhz/qingdan/{appId}.zip");
            if (!resp.IsSuccessStatusCode) return null;
            return await ParseZipSourceAsync(appId, source,
                await resp.Content.ReadAsByteArrayAsync());
        }
        catch (Exception ex) { Logger.Debug($"[{source}] {ex.Message}"); return null; }
    }

    // ─── cysaw.top ───────────────────────────────────────────────────────

    private async Task<ManifestResult?> TryCysawAsync(uint appId)
    {
        const string source = "cysaw.top";
        try
        {
            Logger.Debug($"[{source}] trying...");
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://cysaw.top/uploads/{appId}.zip");
            req.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            return await ParseZipSourceAsync(appId, source,
                await resp.Content.ReadAsByteArrayAsync());
        }
        catch (Exception ex) { Logger.Debug($"[{source}] {ex.Message}"); return null; }
    }

    // ─── Zip / .st / .lua / Key.vdf parser ──────────────────────────────

    private async Task<ManifestResult?> ParseZipSourceAsync(
        uint appId, string source, byte[] zipBytes)
    {
        var result = new ManifestResult { AppId = appId, Source = source };
        using var ms  = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        foreach (var entry in zip.Entries)
        {
            string name = Path.GetFileName(entry.FullName);

            if (name.EndsWith(".manifest"))
            {
                var parts = Path.GetFileNameWithoutExtension(name).Split('_');
                if (parts.Length >= 2 &&
                    uint.TryParse(parts[0],  out uint  dId) &&
                    ulong.TryParse(parts[1], out ulong mId))
                {
                    using var s = entry.Open(); using var buf = new MemoryStream();
                    await s.CopyToAsync(buf);
                    result.Manifests[dId] = new ManifestEntry
                        { DepotId = dId, ManifestId = mId, Data = buf.ToArray() };
                    Logger.Debug($"[{source}] manifest {name} ✓");
                }
            }
            else if (name.EndsWith(".st"))
            {
                using var s = entry.Open(); using var buf = new MemoryStream();
                await s.CopyToAsync(buf);
                ParseStFile(appId, buf.ToArray(), result);
            }
            else if (name.EndsWith(".lua"))
            {
                using var s  = entry.Open();
                using var sr = new StreamReader(s, Encoding.UTF8);
                ParseLuaContent(appId, await sr.ReadToEndAsync(), result);
            }
            else if (name.ToLowerInvariant() is "key.vdf" or "config.vdf")
            {
                using var s = entry.Open(); using var buf = new MemoryStream();
                await s.CopyToAsync(buf);
                ParseKeyVdf(buf.ToArray(), result, null);
            }
        }

        return result.Manifests.Count > 0 || result.DepotKeys.Count > 0 ? result : null;
    }

    // ─── .st parser ──────────────────────────────────────────────────────

    private void ParseStFile(uint appId, byte[] data, ManifestResult result)
    {
        try
        {
            if (data.Length < 12) return;
            uint xorKey = BitConverter.ToUInt32(data, 0);
            uint size   = BitConverter.ToUInt32(data, 4);
            xorKey ^= 0xFFFEA4C8; xorKey &= 0xFF;
            byte[] body = new byte[size];
            Array.Copy(data, 12, body, 0, (int)Math.Min(size, data.Length - 12));
            for (int i = 0; i < body.Length; i++) body[i] ^= (byte)xorKey;
            byte[] dec = Decompress(body);
            string lua = Encoding.UTF8.GetString(dec, 512, Math.Max(0, dec.Length - 512));
            ParseLuaContent(appId, lua, result);
        }
        catch (Exception ex) { Logger.Debug($"ParseStFile: {ex.Message}"); }
    }

    // ─── Lua parser ──────────────────────────────────────────────────────

    private static readonly Regex AddAppIdRx = new(
        @"addappid\(\s*(\d+)\s*(?:,\s*\d+\s*,\s*""([0-9a-fA-F]+)""\s*)?\)",
        RegexOptions.Compiled);

    private static readonly Regex SetManifestRx = new(
        @"setManifestid\(\s*(\d+)\s*,\s*""(\d+)""\s*(?:,\s*\d+\s*)?\)",
        RegexOptions.Compiled);

    private static void ParseLuaContent(uint appId, string content, ManifestResult result)
    {
        foreach (Match m in AddAppIdRx.Matches(content))
        {
            if (!uint.TryParse(m.Groups[1].Value, out uint depotId)) continue;
            if (m.Groups[2].Success && !string.IsNullOrEmpty(m.Groups[2].Value))
            {
                result.DepotKeys[depotId] = Convert.FromHexString(m.Groups[2].Value);
                Logger.Debug($"Lua key: depot {depotId}");
            }
        }
        foreach (Match m in SetManifestRx.Matches(content))
        {
            if (!uint.TryParse(m.Groups[1].Value,  out uint  depotId))    continue;
            if (!ulong.TryParse(m.Groups[2].Value, out ulong manifestId)) continue;
            if (!result.Manifests.ContainsKey(depotId))
                result.Manifests[depotId] = new ManifestEntry
                    { DepotId = depotId, ManifestId = manifestId, Data = null };
            Logger.Debug($"Lua manifest: depot {depotId} → {manifestId}");
        }
    }

    // ─── Key.vdf / config.vdf parser ─────────────────────────────────────

    private static void ParseKeyVdf(byte[] data, ManifestResult result, byte[]? xorKey)
    {
        try
        {
            string vdf = Encoding.UTF8.GetString(data);

            // Standard Key.vdf format: "depotId" { "DecryptionKey" "hexkey" }
            var depotRx = new Regex(
                @"""(\d+)""\s*\{[^}]*""DecryptionKey""\s*""([0-9a-fA-F]+)""",
                RegexOptions.Singleline);
            foreach (Match m in depotRx.Matches(vdf))
            {
                if (!uint.TryParse(m.Groups[1].Value, out uint depotId)) continue;
                byte[] key = Convert.FromHexString(m.Groups[2].Value);
                if (xorKey != null) key = XorDecrypt(xorKey, key);
                result.DepotKeys[depotId] = key;
                Logger.Debug($"Key.vdf: depot {depotId}");
            }

            // config.vdf format: "depots" { "depotId" { "DecryptionKey" "hexkey" } }
            // (same regex matches — the outer nesting doesn't matter for regex)
        }
        catch (Exception ex) { Logger.Debug($"ParseKeyVdf: {ex.Message}"); }
    }

    // ─── Crypto ──────────────────────────────────────────────────────────

    private static byte[]? SymmetricDecrypt(byte[] key, byte[] cipher)
    {
        try
        {
            using var ecb = Aes.Create();
            ecb.Key = key; ecb.Mode = CipherMode.ECB; ecb.Padding = PaddingMode.None;
            byte[] iv = new byte[16]; Array.Copy(cipher, iv, 16);
            iv = ecb.CreateDecryptor().TransformFinalBlock(iv, 0, 16);
            using var cbc = Aes.Create();
            cbc.Key = key; cbc.IV = iv; cbc.Mode = CipherMode.CBC; cbc.Padding = PaddingMode.PKCS7;
            return cbc.CreateDecryptor().TransformFinalBlock(cipher, 16, cipher.Length - 16);
        }
        catch (Exception ex) { Logger.Debug($"SymmetricDecrypt: {ex.Message}"); return null; }
    }

    private static byte[] XorDecrypt(byte[] key, byte[] data)
    {
        byte[] r = new byte[data.Length];
        for (int i = 0; i < data.Length; i++) r[i] = (byte)(data[i] ^ key[i % key.Length]);
        return r;
    }

    private static byte[] Decompress(byte[] data)
    {
        using var ms   = new MemoryStream(data);
        using var gz   = new DeflateStream(ms, CompressionMode.Decompress);
        using var out_ = new MemoryStream();
        gz.CopyTo(out_);
        return out_.ToArray();
    }

    // ─── HTTP ─────────────────────────────────────────────────────────────

    private Dictionary<string, string> BuildGitHubHeaders()
    {
        var h = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(_githubToken))
            h["Authorization"] = $"Bearer {_githubToken}";
        return h;
    }

    private async Task<JObject?> FetchJsonAsync(
        string url, Dictionary<string, string> headers)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
            var resp = await _http.SendAsync(req);

            if ((int)resp.StatusCode is 403 or 429)
            {
                if (url.Contains("api.github.com"))
                {
                    Interlocked.Exchange(ref _githubRateLimited, 1);
                    Logger.Warn("GitHub API rate-limited. Use --api-key <github_pat> " +
                                "to raise cap from 60 to 5000 req/hr.");
                }
                return null;
            }
            if (!resp.IsSuccessStatusCode) return null;
            return JObject.Parse(await resp.Content.ReadAsStringAsync());
        }
        catch { return null; }
    }

    private async Task<JArray?> FetchJsonArrayAsync(
        string url, Dictionary<string, string> headers)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
            var resp = await _http.SendAsync(req);
            if ((int)resp.StatusCode is 403 or 429)
            {
                if (url.Contains("api.github.com")) Interlocked.Exchange(ref _githubRateLimited, 1);
                return null;
            }
            if (!resp.IsSuccessStatusCode) return null;
            return JArray.Parse(await resp.Content.ReadAsStringAsync());
        }
        catch { return null; }
    }

    private async Task<byte[]?> FetchRawAsync(string sha, string path, string repo)
    {
        // Multiple CDN mirrors — tries each in order until one responds 200
        string[] urls =
        {
            $"https://raw.githubusercontent.com/{repo}/{sha}/{path}",
            $"https://raw.gitmirror.com/{repo}/{sha}/{path}",
            $"https://cdn.jsdmirror.com/gh/{repo}@{sha}/{path}",
            $"https://raw.dgithub.xyz/{repo}/{sha}/{path}",
            $"https://gh.akass.cn/{repo}/{sha}/{path}",
            $"https://jsdelivr.pai233.top/gh/{repo}@{sha}/{path}",
            $"https://github.moeyy.xyz/https://raw.githubusercontent.com/{repo}/{sha}/{path}",
        };
        foreach (var url in urls)
        {
            try
            {
                using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var resp = await _http.GetAsync(url, cts.Token);
                if (resp.IsSuccessStatusCode)
                    return await resp.Content.ReadAsByteArrayAsync();
            }
            catch { }
        }
        return null;
    }
}

// ─── Result types ─────────────────────────────────────────────────────────────

public class ManifestResult
{
    public uint   AppId  { get; set; }
    public string Source { get; set; } = "";
    public Dictionary<uint, ManifestEntry> Manifests { get; } = new();
    public Dictionary<uint, byte[]>        DepotKeys { get; } = new();
}

public class ManifestEntry
{
    public uint    DepotId    { get; set; }
    public ulong   ManifestId { get; set; }
    public byte[]? Data       { get; set; }
}
