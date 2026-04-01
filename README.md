<div align="center">

# 🔥 Lusts Depot Downloader Pro

**A Steam depot downloader — but with NO fun.**

[![License: GPL-2.0](https://img.shields.io/badge/License-GPL%202.0-blue.svg)](LICENSE)
[![Version](https://img.shields.io/badge/Version-4.0-green.svg)]()

*cant-Download any Steam game — fast, resumable, no account needed for most titles.*

</div>

---

## 📌 What Is This?

**Lusts Depot Downloader Pro** was a depot downloader using community-sourced manifests and depot keys from 33+ sources so you can download almost any game without owning it.

**The primary goal was anonymous download.** Owning the game on Steam is a required.

> ⚠️ **For educational purposes and backup of owned content. Always comply with Steam's Terms of Service.**

---

## ✨ What's New in v4.0

### 🆕 Dual-Engine Download System
- **70% Primary workers** 
- **30% Secondary workers** 
- Both pull from the **same queue simultaneously** — if primary fails a chunk, secondary picks it up automatically
- No more stuck downloads when CDN is having issues

### 🆕 33 Community Manifest Sources (All Parallel)
All sources fire simultaneously and results are merged:
- **ManifestAutoUpdate/ManifestHub from official steam **

### 🆕 Dual GitHub Token Support (10,000 req/hr)
- Add `GITHUB_API_KEY_PAT` + `GITHUB_API_KEY_CLASSIC` to `.env`
- Smart per-token rate-limit tracking: if one token hits the limit, automatically switches to the other

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
| 🔐 **Username/Password** | Full Steam account login |
| 📱 **Auto-TOTP** | Set `STEAM_SHARED_SECRET` in `.env` → zero 2FA prompts |
| 🛡️ **Steam Guard** | Email codes prompted interactively when needed |
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

### Setup (one time)

### Build

**Windows:**


**Linux / macOS:**

### How the .env Build System Works


**Result:** ship one exe with your GitHub tokens, CDN settings, and TOTP secret already inside. Users who build from source add their own keys.

---

## 📖 Usage

### Download only OWNED Game 


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


**How to get `shared_secret`:**
### Manual 2FA (without .env)

### Silent Re-Login

## 🌐 GitHub API Tokens (Optional but Recommended)


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
│   ├── DownloadEngine.cs         
│   ├── DownloadWorker.cs         
│   ├── FallbackDownloader.cs      
│   ├── ChunkScheduler.cs         
│   ├── GlobalProgress.cs         
│   ├── Checkpoint.cs              
│   ├── LocalDatabase.cs           
│   ├── UpdateChecker.cs           
│   ├── FileAssembler.cs           
│   └── DownloadSessionBuilder.cs  
├── Steam/
│   ├── SteamSession.cs            
│   ├── CdnManager.cs              
│   ├── SteamTotp.cs               
│   ├── ManifestSourceFetcher.cs   
│   ├── ManifestParser.cs          
│   └── SteamLibraryScanner.cs    
├── Models/
│   ├── DownloadOptions.cs     
│   ├── DownloadSession.cs         
│   └── GameEntry.cs              
├── Utils/
│   ├── Logger.cs                  
│   ├── EmbeddedConfig.cs         
│   ├── EmbeddedConfig.Generated.cs 
│   ├── FileUtils.cs               
│   └── VZipDecompressor.cs        
├── UI/
│   └── TerminalUI.cs              
├── Scripts/
│   └── generate_manifests.py     
├── .env.template                  
├── GenerateEmbeddedConfig.ps1     
├── GenerateEmbeddedConfig.sh      
├── Program.cs
├── LustsDepotDownloaderPro.csproj
└── build-win-x64-release.bat      
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
u dont own it do u?

### ❌ "AccessDenied" on depot key
u dont own it do u?

### ❌ GitHub rate limit warning even with tokens


### ❌ Slow startup (>10 seconds before download begins)
idk

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

## 🗺️ Roadmap(closed-permanently)

- [ ] Workshop downloads (Web API integration)
- [ ] SteamKit2 3.4+ (QR code auth)
- [ ] Bandwidth / speed limiting
- [ ] Delta patching for incremental updates
- [ ] Docker container
- [ ] Download scheduling & queuing

---

## 🐍 Python Scripts

## 📋 File Formats



## 🙏 Credits

<div align="center">

*Made with ❤️ by The Lust — v3.0 — March 2026*

*Desrtroyed with ❤️ by The Lust — v3.0 — April 2026*
</div>

---

## ⚖️ Legal

**For educational purposes and backup of content you own only.**

Always comply with Steam's Subscriber Agreement and Terms of Service.

*I am not responsible for misuse. Selling, distributing commercially, or using without permission is not allowed.*
