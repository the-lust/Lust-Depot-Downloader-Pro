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
/// Full C# port of storage_depotdownloadermod.py manifest sources.
///
/// FIX v2: FetchAsync() now tries ALL sources and MERGES results.
///   Previously it returned on the first successful source, so if
///   ikun0014/ManifestHub had 3 depots for Wukong and SteamAutoCracks/ManifestHub
///   had 15 more, only those 3 were ever used.
///
/// FIX v2: ParseGobData() was a stub (returned null immediately), breaking
///   luckygametools/steam-cfg entirely. Now uses bundled Python+pygob bridge
///   with a VZ-scanner fallback when Python is unavailable.
///
/// FIX v2: GitHub 403/429 rate-limit responses are detected and reported
///   clearly, explaining the --api-key option.
///
/// FIX v2: Extra CDN mirror URLs for raw GitHub content (better international
///   coverage and resilience when primary mirror is slow).
///
/// Sources (all tried, results merged):
///   1. ikun0014/ManifestHub
///   2. Auiowu/ManifestAutoUpdate
///   3. tymolu233/ManifestAutoUpdate
///   4. SteamAutoCracks/ManifestHub
///   5. sean-who/ManifestAutoUpdate    (XOR-encrypted Key.vdf)
///   6. luckygametools/steam-cfg        (AES+XOR+gob .dat)
///   7. printedwaste.com
///   8. steambox.gdata.fun
///   9. cysaw.top
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

    // Set when GitHub returns 403 / 429 so we stop hammering after the first hit
    private bool _githubRateLimited = false;

    public ManifestSourceFetcher(string? githubToken = null)
    {
        _githubToken = githubToken;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LustsDepotDownloaderPro/1.0");
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// FIX: Try ALL sources and MERGE results instead of returning on first success.
    /// Games like Black Myth Wukong have depots across multiple repos; merging
    /// ensures complete coverage.
    /// </summary>
    public async Task<ManifestResult?> FetchAsync(uint appId)
    {
        Logger.Info($"Searching ALL community manifest sources for app {appId}...");
        _githubRateLimited = false;

        var merged = new ManifestResult { AppId = appId, Source = "merged" };

        // GitHub repos: try ALL of them, merge results
        (string repo, byte[]? xorKey)[] githubSources =
        {
            ("ikun0014/ManifestHub",        null),
            ("Auiowu/ManifestAutoUpdate",   null),
            ("tymolu233/ManifestAutoUpdate",null),
            ("SteamAutoCracks/ManifestHub", null),
            ("sean-who/ManifestAutoUpdate", SeanWhoXorKey),
        };

        foreach (var (repo, xorKey) in githubSources)
        {
            if (_githubRateLimited)
            {
                Logger.Warn("GitHub rate limit hit — skipping remaining GitHub sources. " +
                            "Pass --api-key <github_token> to raise limit to 5000 req/hr.");
                break;
            }

            var result = await TryGitHubBranchAsync(appId, repo, xorKey);
            if (result != null)
            {
                MergeInto(merged, result);
                Logger.Info($"[{repo}] +{result.Manifests.Count} manifest(s), " +
                            $"+{result.DepotKeys.Count} key(s)");
            }
        }

        // luckygametools (AES+XOR encrypted gob blob)
        if (!_githubRateLimited)
        {
            var lucky = await TryLuckyGameToolsAsync(appId);
            if (lucky != null)
            {
                MergeInto(merged, lucky);
                Logger.Info($"[luckygametools] +{lucky.Manifests.Count} manifest(s), " +
                            $"+{lucky.DepotKeys.Count} key(s)");
            }
        }

        // REST / zip sources
        foreach (var fn in new Func<Task<ManifestResult?>>[]
        {
            () => TryPrintedWasteAsync(appId),
            () => TryGdataAsync(appId),
            () => TryCysawAsync(appId),
        })
        {
            try
            {
                var r = await fn();
                if (r != null)
                {
                    MergeInto(merged, r);
                    Logger.Info($"[{r.Source}] +{r.Manifests.Count} manifest(s), " +
                                $"+{r.DepotKeys.Count} key(s)");
                }
            }
            catch (Exception ex) { Logger.Debug($"REST source error: {ex.Message}"); }
        }

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

    // ─── Merge helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Merge source into target.  An entry with binary Data beats one without.
    /// Existing binary entries are never replaced (first-write wins for data).
    /// </summary>
    private static void MergeInto(ManifestResult target, ManifestResult source)
    {
        foreach (var (id, entry) in source.Manifests)
        {
            if (!target.Manifests.TryGetValue(id, out var existing))
                target.Manifests[id] = entry;
            else if (existing.Data == null && entry.Data != null)
                target.Manifests[id] = entry;   // upgrade ID-only to binary
        }
        foreach (var (id, key) in source.DepotKeys)
            if (!target.DepotKeys.ContainsKey(id))
                target.DepotKeys[id] = key;
    }

    // ─── GitHub branch source ─────────────────────────────────────────────────

    private async Task<ManifestResult?> TryGitHubBranchAsync(
        uint appId, string repo, byte[]? xorDecryptKey)
    {
        try
        {
            Logger.Debug($"[{repo}] checking branch {appId}...");
            var headers = BuildGitHubHeaders();

            // 1. Branch info → SHA + tree URL
            var branchUrl  = $"https://api.github.com/repos/{repo}/branches/{appId}";
            var branchJson = await FetchJsonAsync(branchUrl, headers);
            if (branchJson == null || !branchJson.ContainsKey("commit")) return null;

            string sha     = branchJson["commit"]!["sha"]!.ToString();
            string treeUrl = branchJson["commit"]!["commit"]!["tree"]!["url"]!.ToString();

            // 2. Tree → file list
            var treeJson = await FetchJsonAsync(treeUrl, headers);
            if (treeJson == null || !treeJson.ContainsKey("tree")) return null;

            var tree   = treeJson["tree"]!.ToArray();
            var result = new ManifestResult { AppId = appId, Source = repo };

            // 3. Download all .manifest binaries
            foreach (var item in tree)
            {
                string path = item["path"]!.ToString();
                if (!path.EndsWith(".manifest")) continue;

                var parts = Path.GetFileNameWithoutExtension(path).Split('_');
                if (parts.Length < 2)                                 continue;
                if (!uint.TryParse(parts[0],  out uint  depotId))    continue;
                if (!ulong.TryParse(parts[1], out ulong manifestId)) continue;

                byte[]? data = await FetchRawAsync(sha, path, repo);
                if (data == null) continue;

                result.Manifests[depotId] = new ManifestEntry
                {
                    DepotId    = depotId,
                    ManifestId = manifestId,
                    Data       = data
                };
                Logger.Info($"[{repo}] manifest {depotId}_{manifestId} ✓");
            }

            // 4. Depot keys from Key.vdf
            var keyEntry = tree.FirstOrDefault(i =>
                i["path"]!.ToString().ToLowerInvariant() == "key.vdf");
            if (keyEntry != null)
            {
                byte[]? keyData = await FetchRawAsync(sha, keyEntry["path"]!.ToString(), repo);
                if (keyData != null) ParseKeyVdf(keyData, result, xorDecryptKey);
            }

            return result.Manifests.Count > 0 || result.DepotKeys.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            Logger.Debug($"[{repo}] {ex.Message}");
            return null;
        }
    }

    // ─── luckygametools/steam-cfg ─────────────────────────────────────────────

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

            // AES (ECB IV + CBC body) then XOR
            byte[]? aesDecrypted = SymmetricDecrypt(LuckyAesKey, raw);
            if (aesDecrypted == null) return null;
            byte[] xorDecrypted = XorDecrypt(LuckyXorKey, aesDecrypted);

            // FIX: Try Python pygob bridge, fall back to VZ-scanner
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

    /// <summary>
    /// FIX: The original ParseGobData() returned null immediately ("not implemented").
    /// This invokes the bundled Scripts/parse_luckygob.py via Python to decode the
    /// Go gob-encoded AppInfo blob. Requires Python 3 + pygob in the Scripts directory.
    /// </summary>
    private static async Task<ManifestResult?> ParseGobViaPythonAsync(
        uint appId, byte[] gobData)
    {
        // Look for the helper script next to the exe or in Scripts/
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "Scripts", "parse_luckygob.py"),
            Path.Combine(AppContext.BaseDirectory, "parse_luckygob.py"),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "parse_luckygob.py"),
        };
        string? script = candidates.FirstOrDefault(File.Exists);
        if (script == null)
        {
            Logger.Debug("parse_luckygob.py not found in Scripts/ — falling back to VZ scanner");
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
                        File.Delete(tmpIn);
                        File.Delete(tmpOut);
                        return ParseGobJson(appId, json);
                    }
                    else
                    {
                        string err = await proc.StandardError.ReadToEndAsync();
                        Logger.Debug($"parse_luckygob.py [{pythonExe}] exit {proc.ExitCode}: {err.Trim()}");
                    }
                }
                catch { /* try next python exe name */ }
            }

            File.Delete(tmpIn);
            File.Delete(tmpOut);
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
                    if (!string.IsNullOrEmpty(b64))
                        data = Convert.FromBase64String(b64);
                }

                result.Manifests[depotId] = new ManifestEntry
                {
                    DepotId    = depotId,
                    ManifestId = manifestId,
                    Data       = data
                };
            }
            return result;
        }
        catch (Exception ex) { Logger.Debug($"ParseGobJson: {ex.Message}"); return null; }
    }

    /// <summary>
    /// Heuristic fallback: scan the decrypted blob for SteamKit2 VZ magic bytes
    /// and try DepotManifest.Deserialize from each hit. Cannot recover depot IDs
    /// or keys without full gob parsing, so it logs candidates for debugging.
    /// Install Python + pygob for full luckygametools support.
    /// </summary>
    private static ManifestResult? ParseGobViaVzScanner(uint appId, byte[] data)
    {
        Logger.Debug($"[luckygametools] VZ scanner: scanning {data.Length} bytes for manifest magic...");
        var result = new ManifestResult { AppId = appId };
        int found = 0;

        for (int i = 0; i < data.Length - 4; i++)
        {
            // VZ magic = 0x56 0x5A
            if (data[i] != 0x56 || data[i + 1] != 0x5A) continue;

            // Try multiple lengths to find where this manifest ends
            int[] trySizes = { 2 * 1024 * 1024, 1024 * 1024, 512 * 1024, 256 * 1024, 128 * 1024 };
            foreach (int sz in trySizes)
            {
                int end = Math.Min(i + sz, data.Length);
                try
                {
                    var dm = SteamKit2.DepotManifest.Deserialize(data[i..end]);
                    if (dm?.Files == null || dm.Files.Count == 0) continue;
                    found++;
                    Logger.Info($"[luckygametools] VZ hit @{i}: {dm.Files.Count} files " +
                                $"(depot ID unknown — install Python+pygob for full support)");
                    break;
                }
                catch { }
            }
        }

        if (found == 0)
            Logger.Debug("[luckygametools] VZ scanner: no valid manifests found");

        // Return null unless we have actual depot IDs — without them the data is unusable
        return null;
    }

    // ─── printedwaste.com ────────────────────────────────────────────────────

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

    // ─── steambox.gdata.fun ───────────────────────────────────────────────────

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

    // ─── cysaw.top ────────────────────────────────────────────────────────────

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

    // ─── Zip (.st / .lua / .manifest) ────────────────────────────────────────

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
                    uint.TryParse(parts[0], out uint dId) &&
                    ulong.TryParse(parts[1], out ulong mId))
                {
                    using var s = entry.Open(); using var buf = new MemoryStream();
                    await s.CopyToAsync(buf);
                    result.Manifests[dId] = new ManifestEntry
                        { DepotId = dId, ManifestId = mId, Data = buf.ToArray() };
                    Logger.Info($"[{source}] manifest {name} ✓");
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
            else if (name.ToLowerInvariant() == "key.vdf")
            {
                using var s = entry.Open(); using var buf = new MemoryStream();
                await s.CopyToAsync(buf);
                ParseKeyVdf(buf.ToArray(), result, null);
            }
        }

        return result.Manifests.Count > 0 || result.DepotKeys.Count > 0 ? result : null;
    }

    // ─── .st parser ───────────────────────────────────────────────────────────

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

    // ─── Lua / .st text parser ────────────────────────────────────────────────

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

    // ─── Key.vdf parser ───────────────────────────────────────────────────────

    private static void ParseKeyVdf(byte[] data, ManifestResult result, byte[]? xorKey)
    {
        try
        {
            string vdf     = Encoding.UTF8.GetString(data);
            var    depotRx = new Regex(
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
        }
        catch (Exception ex) { Logger.Debug($"ParseKeyVdf: {ex.Message}"); }
    }

    // ─── Crypto ───────────────────────────────────────────────────────────────

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
        using var ms  = new MemoryStream(data);
        using var gz  = new DeflateStream(ms, CompressionMode.Decompress);
        using var out_ = new MemoryStream();
        gz.CopyTo(out_);
        return out_.ToArray();
    }

    // ─── HTTP ─────────────────────────────────────────────────────────────────

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

            // FIX: detect rate limit specifically so we warn the user properly
            if ((int)resp.StatusCode == 403 || (int)resp.StatusCode == 429)
            {
                if (url.Contains("api.github.com"))
                {
                    _githubRateLimited = true;
                    Logger.Warn("GitHub API rate-limited. Use --api-key <github_pat> " +
                                "to raise the cap from 60 to 5000 req/hr.");
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
                if (url.Contains("api.github.com")) _githubRateLimited = true;
                return null;
            }
            if (!resp.IsSuccessStatusCode) return null;
            return JArray.Parse(await resp.Content.ReadAsStringAsync());
        }
        catch { return null; }
    }

    private async Task<byte[]?> FetchRawAsync(string sha, string path, string repo)
    {
        // FIX: extra CDN mirror URLs for resilience and CN coverage
        string[] urls =
        {
            $"https://raw.githubusercontent.com/{repo}/{sha}/{path}",
            $"https://raw.gitmirror.com/{repo}/{sha}/{path}",
            $"https://cdn.jsdmirror.com/gh/{repo}@{sha}/{path}",
            $"https://raw.dgithub.xyz/{repo}/{sha}/{path}",
            $"https://gh.akass.cn/{repo}/{sha}/{path}",
            $"https://jsdelivr.pai233.top/gh/{repo}@{sha}/{path}",
        };
        foreach (var url in urls)
        {
            try
            {
                using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var        resp = await _http.GetAsync(url, cts.Token);
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
