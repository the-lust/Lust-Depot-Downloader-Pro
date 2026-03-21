<div align="center">

# 🔥 Lusts Depot Downloader Pro

**A Steam depot downloader — but with extra fun.**

[![License: GPL-2.0](https://img.shields.io/badge/License-GPL%202.0-blue.svg)](LICENSE)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey.svg)]()
[![Version](https://img.shields.io/badge/Version-3.0-green.svg)]()
[![SteamKit2](https://img.shields.io/badge/SteamKit2-3.2.0-1b2838.svg)]()

*Download any Steam game — fast, resumable, no account needed for most titles.*

</div>

---

## 📌 What Is This?

**Lusts Depot Downloader Pro** is a command-line Steam depot downloader built on [SteamKit2](https://github.com/SteamRE/SteamKit). It downloads game files directly from Steam's CDN — **anonymously or with an account** — using community-sourced manifests and depot keys from 33+ sources so you can download almost any game without owning it.

**The primary goal is anonymous download.** Owning the game on Steam is a secondary fallback — the community manifest system handles everything else.

> ⚠️ **For educational purposes and backup of owned content. Always comply with Steam's Terms of Service.**

---

## ✨ What's New in v3.0

### 🆕 Dual-Engine Download System
- **70% Primary workers** (SteamKit2 CDN — authenticated, LZMA-aware)
- **30% Secondary workers** (direct HTTP fallback — `Bearer /depot/{id}/chunk/{hex}`)
- Both pull from the **same queue simultaneously** — if primary fails a chunk, secondary picks it up automatically
- No more stuck downloads when CDN is having issues

### 🆕 33 Community Manifest Sources (All Parallel)
All sources fire simultaneously and results are merged:
- **13 GitHub ManifestAutoUpdate/ManifestHub repos** — ikun0014, Auiowu, tymolu233, SteamAutoCracks, sean-who, BlankTMing, wxy1343, pjy612, P-ToyStore, isKoi, yunxiao6, BlueAmulet, masqueraigne + more
- **luckygametools/steam-cfg** — AES+XOR encrypted depot data
- **printedwaste.com, steambox.gdata.fun, cysaw.top** — REST/ZIP sources
- **29 total GitHub repos + 3 REST endpoints + luckygametools = 33+ sources**

### 🆕 Dual GitHub Token Support (10,000 req/hr)
- Add `GITHUB_API_KEY_PAT` + `GITHUB_API_KEY_CLASSIC` to `.env`
- Each token has 5,000 req/hr — two tokens = **10,000 req/hr combined**
- Smart per-token rate-limit tracking: if one token hits the limit, automatically switches to the other
- Proper 403 vs rate-limit detection (checks `X-RateLimit-Remaining` header — no more false warnings)

### 🆕 Auto-TOTP (No More 2FA Prompts)
- Add your Steam Guard `shared_secret` to `.env`
- 2FA codes generated automatically — **zero manual entry**
- Includes Steam time server sync for clock skew correction

### 🆕 DB-Backed Progress (No More Checkpoint Files)
- All progress stored in `%APPDATA%\LustsDepotDownloader\localdb.json`
- No scattered `checkpoint_*.json` files next to game folders
- Resume with just `--app` and `--output` — no file path needed

### 🆕 14-Day Depot Key Cache
- Depot keys cached to disk after first fetch
- Re-used for 14 days before re-requesting from Steam
- Saves time and API calls on repeat downloads

### 🆕 Silent Re-Login (Refresh Tokens)
- After first login with `--remember-password`, the session token is saved
- Future runs re-authenticate silently — no password prompt

### 🆕 Update Checker
- `--updates` checks if any downloaded game has newer manifest IDs available
- Works per-game or across all recorded downloads at once

### 🆕 Build-Time Config (`.env` System)
- All API keys, tokens, and secrets baked into the binary at compile time
- Users never need to pass `--api-key` manually
- Values come from `.env` → compiled into `EmbeddedConfig.Generated.cs` → baked into exe
- `.env` is gitignored — **never committed, never shipped**

---

## ✨ Full Feature List

### Core ✅
| Feature | Details |
|---|---|
| 🚀 **Multi-threaded** | 1–64 parallel workers |
| ⏸️ **Pause & Resume** | DB-backed progress — just `--resume`, no file path needed |
| 🌐 **CDN Failover** | 20+ Steam CDN servers, auto-switches on failure |
| 📄 **33 Manifest Sources** | All fire in parallel and merge results |
| 🔑 **Depot Keys** | From community sources, user file, or Steam API |
| 🔄 **Dual Download Engine** | Primary + secondary workers cooperate on same queue |
| ✅ **Checksum Validation** | `--validate` verifies integrity after download |
| 🎨 **Clean Terminal UI** | Progress bar, filename, speed — nothing else |
| 📦 **Single Executable** | Self-contained, zero dependencies |
| 🖥️ **Cross-Platform** | Windows, Linux, macOS |

### Authentication ✅
| Feature | Details |
|---|---|
| 🔓 **Anonymous** | No account needed for most games via community sources |
| 🔐 **Username/Password** | Full Steam account login |
| 📱 **Auto-TOTP** | Set `STEAM_SHARED_SECRET` in `.env` → zero 2FA prompts |
| 🛡️ **Steam Guard** | Email codes prompted interactively when needed |
| 🔁 **Refresh Tokens** | Silent re-login after first `--remember-password` |
| 💾 **Credential Manager** | Saved credentials reused automatically |

### Advanced ✅
| Feature | Details |
|---|---|
| 🎯 **Platform Filtering** | OS and architecture-specific depot selection |
| 🌍 **Language Selection** | Download only your language's depots |
| 📊 **Real-time Stats** | Live MB/s, %, ETA |
| 🔄 **Auto Retry** | Exponential backoff on chunk failures |
| 🐍 **Python Scripts** | Manifest and key generation from community sources |
| 🗂️ **Local Database** | Game records, update tracking, download history |
| 🔔 **Update Detection** | Check if downloaded games have newer versions |
| 🔧 **GUI-Ready CLI** | Parse-friendly output for wrapping in GUI apps |

---

## 🛠️ Building from Source

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Python 3.8+ *(optional, for manifest scripts only)*

### Setup (one time)

```bash
# 1. Copy the config template
cp .env.template .env

# 2. Fill in .env with your values (all optional — tool works without any)
# See .env.template for full documentation of each field
```

### Build

**Windows:**
```batch
build-win-x64-release.bat
# Output: publish\win-x64-release\LustsDepotDownloaderPro.exe
```

**Linux / macOS:**
```bash
chmod +x build.sh && ./build.sh
# Output: publish/linux-x64-release/LustsDepotDownloaderPro
```

### How the .env Build System Works

Values in `.env` are baked into the binary at compile time — no config files needed at runtime:

```
.env (your secrets)
  │
  └──> GenerateEmbeddedConfig.ps1  (runs before every build)
         │
         └──> EmbeddedConfig.Generated.cs  (C# constants, gitignored)
                │
                └──> baked into the exe
```

**Result:** ship one exe with your GitHub tokens, CDN settings, and TOTP secret already inside. Users who build from source add their own keys.

---

## 📖 Usage

### Download Any Game (Anonymous — No Account Needed)

```bash
# Most games work without an account — community sources provide manifests + keys
LustsDepotDownloaderPro --app 2358720 --output "D:\Games"

# More workers = faster download
LustsDepotDownloaderPro --app 2358720 --output "D:\Games" --max-downloads 32
```

### Download a Game You Own

```bash
# With Steam account (required for some paid games not in community sources)
LustsDepotDownloaderPro --app 730 --username myuser --password mypass --output "D:\Games"
```

### Resume a Download

```bash
# Just pass --resume — no checkpoint file path needed
LustsDepotDownloaderPro --app 2358720 --output "D:\Games" --resume

# Or with more workers
LustsDepotDownloaderPro --app 2358720 --output "D:\Games" --resume --max-downloads 32
```

### Check for Game Updates

```bash
# Check one game
LustsDepotDownloaderPro --updates --app 2358720

# Check all downloaded games
LustsDepotDownloaderPro --updates
```

### View Downloaded Games

```bash
# Show everything in your local database
LustsDepotDownloaderPro --games
```

### Set Preferred CDN Region (persists)

```bash
# Saved — all future downloads use this region automatically
LustsDepotDownloaderPro --cdn-region 4
```

---

## ⚡ Speed Guide

| Workers | Best For | Expected Speed |
|---------|----------|----------------|
| `8` *(default)* | Safe baseline | ~3–5 MB/s |
| `16` | Good broadband | ~5–10 MB/s |
| `32` ✅ | **Recommended sweet spot** | ~8–20 MB/s |
| `64` | Fast fibre (may hit CDN rate limit) | ~15–30+ MB/s |

With v3.0's dual-engine system, the 30% secondary workers keep downloading even when CDN servers are being flaky, so real-world throughput is more consistent than before.

---

## ⏸️ Pause & Resume

Press **Ctrl+C once** to pause cleanly. Progress is saved to the local DB automatically.

```
⏸ Pausing — saving progress...
Resume with:
  --app 2358720 --output "D:\Games" --resume
```

Press **Ctrl+C twice** to force quit immediately.

> Unlike v1.x, there are no checkpoint JSON files. Progress is stored in `%APPDATA%\LustsDepotDownloader\localdb.json` and looked up automatically from your `--app` + `--output` combination.

---

## 🔐 Authentication

### Auto-TOTP (Recommended)

Add to `.env` before building:
```env
STEAM_SHARED_SECRET=your_shared_secret_base64_here
```

2FA codes are generated automatically — no manual entry ever.

**How to get `shared_secret`:**
1. Export your authenticator from [Steam Desktop Authenticator](https://github.com/Jessecar96/SteamDesktopAuthenticator) or WinAuth
2. Open the `.maFile` in a text editor
3. Copy the `"shared_secret"` value

### Manual 2FA (without .env)

```bash
LustsDepotDownloaderPro --app 730 --username myuser --password mypass --output "D:\Games"
# Prompted automatically:
# Steam Mobile Authenticator code: ______
```

### Silent Re-Login

```bash
# First time — saves a refresh token to the DB
LustsDepotDownloaderPro --app 730 --username myuser --password mypass \
  --remember-password --output "D:\Games"

# Every time after — no password prompt
LustsDepotDownloaderPro --app 730 --username myuser --output "D:\Games"
```

---

## 🌐 GitHub API Tokens (Optional but Recommended)

Without tokens: **60 req/hr** (anonymous GitHub limit).  
With tokens: **up to 10,000 req/hr**.

Add to `.env` before building:
```env
GITHUB_API_KEY_PAT=ghp_your_fine_grained_token_here
GITHUB_API_KEY_CLASSIC=ghp_your_classic_token_here
```

Get tokens at [github.com/settings/tokens](https://github.com/settings/tokens) — **no scopes or permissions needed**, just generate and copy.

The tool uses both tokens simultaneously in round-robin. If one hits its limit, it automatically switches to the other with no interruption to the download.

Alternatively, pass at runtime (not baked in):
```bash
LustsDepotDownloaderPro --app 730 --api-key ghp_xxx --output "D:\Games"
```

---

## 🎛️ All Command Line Options

### Download

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--app` | `-a` | AppID to download | required |
| `--depot` | `-d` | Specific DepotID (all if omitted) | — |
| `--manifest` | `-m` | Specific Manifest ID | — |
| `--output` | `-o` | Output directory | current dir |
| `--max-downloads` | `-md` | Parallel workers (1–64) | `32` |
| `--resume` | `-r` | Resume saved progress | `false` |
| `--validate` | `-v` | Verify checksums after download | `false` |
| `--branch` | `-b` | Branch name | `public` |
| `--branch-password` | `-bp` | Branch password | — |

### Authentication

| Option | Short | Description |
|--------|-------|-------------|
| `--username` | `-u` | Steam username |
| `--password` | `-p` | Steam password |
| `--remember-password` | `-rp` | Save refresh token for silent re-login |
| `--api-key` | `-key` | GitHub API token (overrides baked-in .env value) |

### Depot & Keys

| Option | Short | Description |
|--------|-------|-------------|
| `--depot-keys` | `-dk` | Depot keys file (`depotID;hexKey` format) |
| `--manifest-file` | `-mf` | Local manifest file override |
| `--app-token` | `-at` | App access token |

### Filtering

| Option | Short | Description |
|--------|-------|-------------|
| `--filelist` | `-fl` | File filter list (wildcards + regex) |
| `--os` | | Target OS (`windows`/`macos`/`linux`) |
| `--os-arch` | `-arch` | Architecture (`32`/`64`) |
| `--language` | `-lang` | Language depot filter |
| `--all-platforms` | `-ap` | Include all platform depots |
| `--all-languages` | `-al` | Include all language depots |

### Info & Management

| Option | Description |
|--------|-------------|
| `--games` / `--db` | List all recorded games in the local database |
| `--updates` | Check for updates (use with `--app` for one game, alone for all) |
| `--cdn-region <id>` | Set preferred CDN region ID (saved permanently) |
| `--status` / `-s` | Show current download status |
| `--manifest-only` | Print manifest IDs without downloading |
| `--debug` | Verbose debug logging |

### Misc

| Option | Description |
|--------|-------------|
| `--cellid` / `-c` | Override CDN cell ID |
| `--loginid` / `-lid` | Steam Login ID (for running multiple instances) |
| `--all-archs` / `-aa` | Include all architecture depots |
| `--low-violence` / `-lv` | Low-violence depots only |

---

## 📁 Project Structure

```
LustsDepotDownloaderPro/
├── Core/
│   ├── DownloadEngine.cs          # Worker orchestration (70% primary / 30% secondary split)
│   ├── DownloadWorker.cs          # Per-worker dual-engine chunk download
│   ├── FallbackDownloader.cs      # Direct HTTP fallback engine (AES+VZip pipeline)
│   ├── ChunkScheduler.cs          # Thread-safe chunk queue (shared by both engines)
│   ├── GlobalProgress.cs          # Rolling-window speed + ETA tracking
│   ├── Checkpoint.cs              # DB-backed progress (no more .json files)
│   ├── LocalDatabase.cs           # Persistent DB — progress, records, settings
│   ├── UpdateChecker.cs           # Manifest ID comparison for update detection
│   ├── FileAssembler.cs           # High-performance cached file writing
│   └── DownloadSessionBuilder.cs  # Session prep & manifest loading
├── Steam/
│   ├── SteamSession.cs            # Auth (anon/password/TOTP/refresh token)
│   ├── CdnManager.cs              # CDN tokens, 14-day key cache, server selection
│   ├── SteamTotp.cs               # TOTP auth code generator (port of node-steam-totp)
│   ├── ManifestSourceFetcher.cs   # 33 community sources, dual-token, parallel fetch
│   ├── ManifestParser.cs          # Binary manifest decoding
│   └── SteamLibraryScanner.cs     # Local Steam ACF scanner
├── Models/
│   ├── DownloadOptions.cs         # CLI option model
│   ├── DownloadSession.cs         # Session state model
│   └── GameEntry.cs               # DB record models (LocalGameRecord, DownloadRecord)
├── Utils/
│   ├── Logger.cs                  # Structured logging (QuietMode + SilentMode)
│   ├── EmbeddedConfig.cs          # Compile-time config (from .env)
│   ├── EmbeddedConfig.Generated.cs # Auto-generated from .env — gitignored
│   ├── FileUtils.cs               # File helpers, CredentialManager
│   └── VZipDecompressor.cs        # Steam VZip/zlib decompression
├── UI/
│   └── TerminalUI.cs              # Spectre.Console progress bar
├── Scripts/
│   └── generate_manifests.py      # Python manifest/key generator
├── .env.template                  # Config template — copy to .env and fill in
├── GenerateEmbeddedConfig.ps1     # Windows: reads .env, writes Generated.cs
├── GenerateEmbeddedConfig.sh      # Linux/macOS equivalent
├── Program.cs
├── LustsDepotDownloaderPro.csproj
└── build-win-x64-release.bat      # (and other platform build scripts)
```

---

## 🔧 GUI Integration

```csharp
var process = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = "LustsDepotDownloaderPro.exe",
        Arguments = $"--app {appId} --output \"{outputDir}\" --max-downloads 32",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    }
};

// Log format (debug mode): [HH:mm:ss.fff] [LEVEL] Message
// Normal mode: only progress bar output + completion message
process.OutputDataReceived += (s, e) => ParseProgress(e.Data);
process.Start();
process.BeginOutputReadLine();
await process.WaitForExitAsync();
```

**Exit codes:**
| Code | Meaning |
|------|---------|
| `0` | ✅ Download complete |
| `1` | ❌ Error |
| `2` | ⏸️ Paused — progress saved to DB |

---

## 🚨 Troubleshooting

### ❌ Game not downloading / no depots found
- The game may not be in any community repo yet — check [SteamDB](https://www.steamdb.info) for the AppID
- Try with a GitHub token: `--api-key ghp_xxx` (or add to `.env`)
- For paid games not in community sources: `--username myuser --password mypass`

### ❌ "AccessDenied" on depot key
- This depot requires account ownership
- The main game depot probably downloaded fine — these are usually language/variant depots
- Login with `--username`/`--password` if you own the game

### ❌ GitHub rate limit warning even with tokens
- Make sure `GITHUB_API_KEY_PAT` and `GITHUB_API_KEY_CLASSIC` in `.env` are non-empty before building
- Rebuild after updating `.env` — values are baked in at compile time
- Pass `--api-key` at runtime to override: `--api-key ghp_xxx`

### ❌ Slow startup (>10 seconds before download begins)
- This is normal for large games — the app is fetching manifests from 33 sources in parallel
- Should take under 5 seconds with tokens, up to 15 seconds without

### ❌ "Failed to connect to Steam"
- Check internet / firewall
- Try `--cellid 0` to let Steam pick the best server
- Steam servers may be temporarily down

### 🐢 Download is slow
- Default workers is `32` — if you set it lower, increase it
- Try `--max-downloads 32` or `--max-downloads 64`
- The secondary workers (fallback engine) may be slower on some connections

---

## 📝 Known Limitations

| Feature | Status | Workaround |
|---------|--------|------------|
| QR Code Auth | ❌ SteamKit2 3.2.0 limitation | Use `--username`/`--password` |
| Workshop Downloads | ❌ Not implemented | Needs Web API integration |
| LZMA chunks via fallback engine | ⚠️ Routes to primary | Primary engine handles LZMA via SteamKit2 |
| Bandwidth limiting | ❌ Not implemented | Use OS-level tools |

---

## 🗺️ Roadmap

- [ ] Workshop downloads (Web API integration)
- [ ] SteamKit2 3.4+ (QR code auth)
- [ ] Bandwidth / speed limiting
- [ ] Delta patching for incremental updates
- [ ] Docker container
- [ ] Download scheduling & queuing

---

## 🐍 Python Scripts

Generate depot keys and manifests from community sources manually:

```bash
cd Scripts
pip install -r requirements.txt

# Generate everything for an app
python generate_manifests.py 730

# Keys only
python generate_manifests.py 730 --keys-only

# List available manifests
python generate_manifests.py 730 --list-manifests
```

---

## 📋 File Formats

### Depot Keys (`depot_keys.txt`)
```
# Format: depotID;hexKey
731;E5A1D6C2F8B3A4E9D7C1F2A8B4C6E3D9
732;A4B9E3F7C2D8A1E6B3F9C4D7E2A8B1C6
```

### File Filter List (`filelist.txt`)
```
# Wildcards
*.dll
*.exe
maps/*.bsp

# Regex (prefix with regex:)
regex:^models/.*\.(mdl|vtx|vvd)$

# Exact paths
game/csgo.exe
```

---

## 🙏 Credits

<div align="center">

*Made with ❤️ by The Lust — v3.0 — March 2026*

*Built on [SteamKit2](https://github.com/SteamRE/SteamKit)*

*Thanks to [oureveryday](https://github.com/oureveryday) for the foundational work*

*Community manifest sources: ikun0014, BlankTMing, pjy612, SteamAutoCracks, sean-who, Auiowu, tymolu233, wxy1343, and many more*

</div>

---

## ⚖️ Legal

**For educational purposes and backup of content you own only.**

Always comply with Steam's Subscriber Agreement and Terms of Service.

*I am not responsible for misuse. Selling, distributing commercially, or using without permission is not allowed.*
