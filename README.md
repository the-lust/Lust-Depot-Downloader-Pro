<div align="center">

<img src="https://capsule-render.vercel.app/api?type=waving&color=gradient&customColorList=6,11,20&height=200&section=header&text=Lusts%20Depot%20Downloader%20Pro&fontSize=40&fontColor=fff&animation=twinkling&fontAlignY=38&desc=Steam%20Depot%20Downloading%2C%20Supercharged.&descAlignY=60&descSize=16" width="100%"/>

<br/>

[![License](https://img.shields.io/badge/License-Source%20Available-blueviolet?style=for-the-badge&logo=bookstack)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/Windows%20%7C%20Linux%20%7C%20macOS-supported-0078D6?style=for-the-badge&logo=windows)]()
[![Version](https://img.shields.io/badge/Version-1.0.0-22c55e?style=for-the-badge&logo=github)]()
[![SteamKit2](https://img.shields.io/badge/Powered%20By-SteamKit2-1b2838?style=for-the-badge&logo=steam)](https://github.com/SteamRE/SteamKit)

<br/>

> ***A Steam depot downloader — but with extra fun.***
> *Fast. Resumable. Multi-threaded. Beautiful.*

<br/>

</div>

---

## 🧭 Navigation

| | | |
|---|---|---|
| [📌 What Is This?](#-what-is-this) | [⚡ Quick Start](#-quick-start) | [✨ Features](#-features) |
| [🚀 Workers & Speed](#-workers--speed-explained) | [🔧 Building](#️-building-from-source) | [📖 Usage](#-usage) |
| [🎛️ All Options](#️-all-command-line-options) | [🐍 Python Scripts](#-python-scripts) | [🚨 Troubleshooting](#-troubleshooting) |

---

## 📌 What Is This?

**Lusts Depot Downloader Pro** is a command-line Steam depot downloader built on [SteamKit2](https://github.com/SteamRE/SteamKit). It downloads game depots directly from Steam's CDN — anonymously or with a Steam account — with multi-threading, pause/resume, CDN failover, community manifest support, and a clean terminal UI.

```
It's a depot downloader. With extra fun.
```

> ⚠️ **For educational purposes and backup of content you own. Always comply with Steam's Terms of Service.**

---

## ⚡ Quick Start

```batch
:: Download a game anonymously
LustsDepotDownloaderPro.exe --app 730 --output "C:\Games\CSGO"

:: Download with Steam account
LustsDepotDownloaderPro.exe --app 730 --username myuser --password mypass --output "C:\Games\CSGO"

:: Resume a paused download
LustsDepotDownloaderPro.exe --app 730 --output "C:\Games\CSGO" --resume "C:\Games\CSGO\730_CSGO\checkpoint_730.json"

:: Max speed (32 workers — sweet spot)
LustsDepotDownloaderPro.exe --app 730 --output "C:\Games\CSGO" --max-downloads 32
```

---

## ✨ Features

<table>
<tr>
<td width="50%" valign="top">

### ⚙️ Core
- 🚀 **Multi-threaded** — 1 to 64 parallel workers
- ⏸️ **Pause & Resume** — checkpoint system, never re-download
- 🌐 **CDN Failover** — auto-switches across 20+ CDN servers
- 📄 **Community Manifests** — pulls from ManifestAutoUpdate sources
- 🔑 **Depot Key Files** — load keys for encrypted depots
- 🎯 **Workshop Items** — by PublishedFileId or UGC ID
- 🌿 **Branch Support** — public, beta, or any custom branch
- 🔍 **File Filtering** — wildcard & regex support
- ✅ **Checksum Validation** — verify integrity after download
- 📦 **Single Executable** — zero dependencies, self-contained
- 🖥️ **Cross-Platform** — Windows, Linux, macOS

</td>
<td width="50%" valign="top">

### 🔬 Advanced
- 🔐 **Full Auth** — anonymous, password, Steam Guard, 2FA, QR code
- 🎯 **Platform Filtering** — Windows/Linux/macOS specific depots
- 🌍 **Language Selection** — download only what you need
- 📊 **Live Stats** — real-time MB/s, ETA, progress bars
- 🔄 **Auto Retry** — exponential backoff on chunk failures
- 💾 **Low Memory Mode** — lazy scheduling for massive games
- 🐍 **Python Scripts** — manifest & key generation
- 🎨 **Terminal UI** — Spectre.Console powered, beautiful output
- 🔧 **GUI-Ready** — structured output for wrapping in apps
- 💡 **Smart File Writing** — cached handles, 1MB buffers, no flush overhead
- 🧩 **Checkpoint Awareness** — skips already-completed chunks on resume

</td>
</tr>
</table>

---

## 🚀 Workers & Speed Explained

Understanding workers is the key to getting the most out of this tool.

### 🤔 What Are Workers?

Workers are **parallel download threads**. Each worker independently:

```
1. Picks a chunk from the shared download queue
2. Downloads it from a Steam CDN server
3. Decrypts it using the depot key
4. Decompresses it (VZip / zlib)
5. Writes it to disk at the correct byte offset
6. Marks it complete in the checkpoint file
7. Goes back to step 1
```

Think of it like a grocery store checkout — more cashiers (workers) means faster throughput. But open too many registers and the car park (CDN connection pool) gets jammed and people start leaving.

### 📊 How Many Workers Should I Use?

| Workers | Best For | Expected Speed |
|:-------:|----------|:--------------:|
| `1–4` | Slow, shared, or metered connections | ~1–2 MB/s |
| `8` *(default)* | Safe baseline for any connection | ~3–5 MB/s |
| `16` | Good home broadband | ~5–10 MB/s |
| `32` ✅ | **Sweet spot — recommended for most** | ~8–20 MB/s |
| `64` | Fast fibre — may trigger CDN rate limits | ~15–30+ MB/s |

> 💡 **Real talk:** Above 32 workers, Steam's CDN may start rate-limiting you. You'll see `"operation was canceled"` warnings and wasted retries. You often get the same or better speed at 32 vs 64.

### 🧪 Why Is My Speed Not Higher?

Speed is capped by whichever of these bottlenecks hits first:

| Bottleneck | Cause | Fix |
|---|---|---|
| Too few workers | Default 8 won't saturate fast connections | Increase `--max-downloads` |
| CDN rate limiting | Too many workers flooding a single server | Drop to `--max-downloads 32` |
| Your internet | Your ISP or connection limit | Nothing to do |
| Steam CDN for that region | Some regions get slower CDN servers | Try `--cellid` |
| Disk speed | Rarely an issue on SSD | Use SSD output path |

### ⚡ Speed Command Reference

```batch
:: Find your sweet spot:
LustsDepotDownloaderPro.exe --app 730 --output "D:\Games" --max-downloads 16
LustsDepotDownloaderPro.exe --app 730 --output "D:\Games" --max-downloads 32
LustsDepotDownloaderPro.exe --app 730 --output "D:\Games" --max-downloads 64

:: If you see lots of "operation canceled" warnings, step back:
LustsDepotDownloaderPro.exe --app 730 --output "D:\Games" --max-downloads 32
```

---

## 🔧 Building from Source

### Prerequisites

- [**.NET 8.0 SDK**](https://dotnet.microsoft.com/download/dotnet/8.0) — required
- **Python 3.8+** — optional, for manifest scripts only

### Windows

```batch
cd LustsDepotDownloaderPro
build.bat
:: Output → publish\win-x64\LustsDepotDownloaderPro.exe
```

### Linux / macOS

```bash
cd LustsDepotDownloaderPro
chmod +x build.sh && ./build.sh
# Output → publish/linux-x64/LustsDepotDownloaderPro
```

### Manual Build

```bash
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish/win-x64
```

---

## 📖 Usage

### 🎮 Basic Downloads

```batch
:: Anonymous (works for free-to-play / public depots)
LustsDepotDownloaderPro.exe --app 730 --output "C:\Games\CSGO"

:: With Steam account (required for owned paid games)
LustsDepotDownloaderPro.exe --app 730 --username myuser --password mypass --output "C:\Games\CSGO"

:: Specific depot + manifest ID
LustsDepotDownloaderPro.exe --app 730 --depot 731 --manifest 7617088375292372759 ^
  --depot-keys depot_keys.txt --output "C:\Games\CSGO"

:: Workshop item
LustsDepotDownloaderPro.exe --app 730 --pubfile 1885082371 --output "C:\Games\Workshop"

:: Workshop by UGC ID
LustsDepotDownloaderPro.exe --app 730 --ugc 770604181014286929 --output "C:\Games\Workshop"
```

### ⏸️ Pause & Resume

Press **Ctrl+C once** to pause gracefully — the checkpoint saves automatically.

```batch
:: Checkpoint is always saved at:
:: <output>\<appid>_<AppName>\checkpoint_<appid>.json

:: Resume (ALWAYS include --app and --output):
LustsDepotDownloaderPro.exe --app 730 --output "C:\Games\CSGO" ^
  --resume "C:\Games\CSGO\730_CSGO\checkpoint_730.json"

:: Resume with more speed:
LustsDepotDownloaderPro.exe --app 730 --output "C:\Games\CSGO" ^
  --resume "C:\Games\CSGO\730_CSGO\checkpoint_730.json" --max-downloads 32
```

> ⚠️ **Important:** Always pass `--app` and `--output` when resuming. The checkpoint file only stores which chunks are done — not your original command.

### 🔐 Authentication

```batch
:: QR code login (scan with Steam mobile app)
LustsDepotDownloaderPro.exe --app 730 --username myuser --qr --output "C:\Games"

:: Save credentials so you don't have to type them again
LustsDepotDownloaderPro.exe --app 730 --username myuser --password mypass ^
  --remember-password --output "C:\Games"

:: Beta branch
LustsDepotDownloaderPro.exe --app 730 --branch beta --output "C:\Games\CSGO-Beta"

:: Password-protected branch
LustsDepotDownloaderPro.exe --app 730 --branch staging ^
  --branch-password secretword --output "C:\Games"
```

### 🔍 Filtering

```batch
:: Download only files matching your list
LustsDepotDownloaderPro.exe --app 730 --filelist filelist.txt --output "C:\Games\CSGO"

:: English only
LustsDepotDownloaderPro.exe --app 730 --language english --output "C:\Games\CSGO"

:: Windows 64-bit only
LustsDepotDownloaderPro.exe --app 730 --os windows --os-arch 64 --output "C:\Games\CSGO"

:: Everything — all platforms, all languages
LustsDepotDownloaderPro.exe --app 730 --all-platforms --all-languages --output "C:\Games\CSGO"

:: Verify files after download
LustsDepotDownloaderPro.exe --app 730 --output "C:\Games\CSGO" --validate

:: Debug / verbose logging
LustsDepotDownloaderPro.exe --app 730 --output "C:\Games\CSGO" --debug
```

---

## 🎛️ All Command Line Options

### 📦 Essential

| Option | Short | Description | Example |
|--------|:-----:|-------------|---------|
| `--app` | `-a` | AppID to download | `--app 730` |
| `--depot` | `-d` | Specific DepotID (all depots if omitted) | `--depot 731` |
| `--manifest` | `-m` | Specific Manifest ID | `--manifest 7617088375292372759` |
| `--output` | `-o` | Output directory | `--output "C:\Games"` |

### 🔐 Authentication

| Option | Short | Description | Example |
|--------|:-----:|-------------|---------|
| `--username` | `-u` | Steam username | `--username myuser` |
| `--password` | `-p` | Steam password | `--password mypass` |
| `--qr` | | QR code login (Steam mobile) | `--qr` |
| `--remember-password` | `-rp` | Save credentials for next run | `--remember-password` |

### 📄 Depot & Manifest

| Option | Short | Description | Example |
|--------|:-----:|-------------|---------|
| `--depot-keys` | `-dk` | Depot keys file | `--depot-keys keys.txt` |
| `--manifest-file` | `-mf` | Local manifest file | `--manifest-file game.manifest` |
| `--app-token` | `-at` | App access token | `--app-token 1234567890` |
| `--branch` | `-b` | Branch name | `--branch beta` |
| `--branch-password` | `-bp` | Branch password | `--branch-password secret` |

### ⚡ Download Control

| Option | Short | Description | Default |
|--------|:-----:|-------------|:-------:|
| `--max-downloads` | `-md` | Concurrent workers (1–64) | `8` |
| `--resume` | `-r` | Resume from checkpoint file | — |
| `--validate` | `-v` | Verify checksums after download | `false` |
| `--pause` | | Gracefully pause active download | — |
| `--status` | `-s` | Show current download status | — |

### 🔍 Filtering

| Option | Short | Description | Example |
|--------|:-----:|-------------|---------|
| `--filelist` | `-fl` | File filter list | `--filelist filters.txt` |
| `--os` | | Target OS (`windows`/`macos`/`linux`) | `--os windows` |
| `--os-arch` | `-arch` | Architecture (`32`/`64`) | `--os-arch 64` |
| `--language` | `-lang` | Specific language | `--language english` |
| `--all-platforms` | `-ap` | All platform depots | `--all-platforms` |
| `--all-languages` | `-al` | All language depots | `--all-languages` |

### 🎮 Workshop

| Option | Short | Description | Example |
|--------|:-----:|-------------|---------|
| `--pubfile` | `-pf` | Workshop item by PublishedFileId | `--pubfile 1885082371` |
| `--ugc` | | Workshop item by UGC ID | `--ugc 770604181014286929` |

### 🔧 Misc

| Option | Short | Description |
|--------|:-----:|-------------|
| `--debug` | | Verbose debug logging |
| `--terminal-ui` | `-tui` | Live terminal UI (on by default) |
| `--cellid` | `-c` | Override Steam CDN cell ID |
| `--loginid` | `-lid` | Steam Login ID (run multiple instances) |
| `--api-key` | `-key` | GitHub API key for community manifests |

---

## 📋 File Formats

### Depot Keys (`depot_keys.txt`)

```
# Format: depotID;hexKey
# Lines starting with # are ignored

731;E5A1D6C2F8B3A4E9D7C1F2A8B4C6E3D9
732;A4B9E3F7C2D8A1E6B3F9C4D7E2A8B1C6
733;C6E2A9D3F8B1C4E7A2D9F3B8C1E6A4D7
```

### File Filter List (`filelist.txt`)

```
# Wildcards
*.dll
*.exe
maps/*.bsp
bin/*

# Regex (prefix with regex:)
regex:^models/.*\.(mdl|vtx|vvd)$

# Exact file paths
game/csgo.exe
game/engine.dll
```

### Checkpoint File *(auto-generated — do not edit)*

```json
{
  "CompletedChunks": ["abc123...", "def456..."],
  "LastSaved": "2026-03-17T10:30:00Z"
}
```

---

## 🐍 Python Scripts

Generate depot keys and manifests from community sources.

```bash
cd Scripts
pip install -r requirements.txt

# Generate everything for an app
python generate_manifests.py 730

# Keys only
python generate_manifests.py 730 --keys-only

# Generate a ready-to-run Windows batch file
python generate_manifests.py 730 --batch

# List all available manifests
python generate_manifests.py 730 --list-manifests

# Custom output folder
python generate_manifests.py 730 --output my_manifests
```

| Generated File | Description |
|---|---|
| `depot_keys_<appid>.txt` | Depot keys in `depotID;hexKey` format |
| `manifests_<appid>.json` | All available manifest IDs |
| `download_<appid>.bat` | Ready-to-run download script |

---

## 🔧 GUI Integration

```csharp
var process = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = "LustsDepotDownloaderPro.exe",
        Arguments = $"--app {appId} --output \"{outputDir}\" --max-downloads 16",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    }
};

// Log format: [HH:mm:ss.fff] [LEVEL] Message
process.OutputDataReceived += (s, e) => UpdateProgressBar(e.Data);
process.Start();
process.BeginOutputReadLine();
await process.WaitForExitAsync();
```

| Exit Code | Meaning |
|:---------:|---------|
| `0` | ✅ Success — download complete |
| `1` | ❌ Error — check logs |
| `2` | ⏸️ Paused — checkpoint saved |

---

## 📁 Project Structure

```
LustsDepotDownloaderPro/
├── Core/
│   ├── DownloadEngine.cs          ← Main orchestration & worker management
│   ├── DownloadWorker.cs          ← Per-worker chunk download logic
│   ├── ChunkScheduler.cs          ← Thread-safe chunk queue
│   ├── GlobalProgress.cs          ← Shared progress tracking
│   ├── Checkpoint.cs              ← Pause/resume state persistence
│   ├── FileAssembler.cs           ← High-performance cached file writing
│   └── DownloadSessionBuilder.cs  ← Session prep & manifest loading
├── Steam/
│   ├── SteamSession.cs            ← Auth, CDN tokens, connection
│   └── ManifestParser.cs          ← Binary manifest decoding
├── Models/
│   ├── DownloadOptions.cs         ← CLI option model
│   └── DownloadSession.cs         ← Session state & checkpoint model
├── Utils/
│   ├── Logger.cs                  ← Structured logging
│   ├── FileUtils.cs               ← File path helpers
│   └── VZipDecompressor.cs        ← Steam VZip decompression
├── UI/
│   └── TerminalUI.cs              ← Spectre.Console live UI
├── Scripts/
│   └── generate_manifests.py      ← Python manifest/key generator
├── Program.cs                     ← Entry point & CLI parsing
├── LustsDepotDownloaderPro.csproj
├── build.bat
├── build.sh
└── README.md
```

---

## 🚨 Troubleshooting

<details>
<summary><b>❌ "Failed to connect to Steam"</b></summary>
<br/>

- Check your internet connection
- Try overriding the CDN cell: `--cellid 0`
- Check if a firewall or antivirus is blocking outbound connections

</details>

<details>
<summary><b>❌ "Failed to get depot key / AccessDenied"</b></summary>
<br/>

- Use `--depot-keys` with a keys file
- Some depots require ownership — try `--username` and `--password`
- Generate keys: `python Scripts/generate_manifests.py <appid> --keys-only`

</details>

<details>
<summary><b>❌ "Manifest not found"</b></summary>
<br/>

- Double-check your AppID and DepotID
- Try `--branch beta` or another branch
- Run `python Scripts/generate_manifests.py <appid> --list-manifests`

</details>

<details>
<summary><b>❌ "All CDN servers failed"</b></summary>
<br/>

- Reduce workers: `--max-downloads 16`
- Check if the game is region-locked
- Your ISP may be throttling Steam traffic

</details>

<details>
<summary><b>⚠️ "file is being used by another process" warnings</b></summary>
<br/>

**Harmless.** Multiple workers briefly contend on the same large file. The worker retries automatically and the file ends up correct. Download continues normally.

</details>

<details>
<summary><b>⚠️ Many "operation was canceled" warnings</b></summary>
<br/>

Too many workers are overwhelming the CDN connection pool. Drop to `--max-downloads 32` — you'll often get the same or better speed with far fewer wasted retries.

</details>

<details>
<summary><b>🐢 Download is slow</b></summary>
<br/>

- Default is only 8 workers — increase to `--max-downloads 32`
- See the [Workers & Speed Explained](#-workers--speed-explained) section above

</details>

<details>
<summary><b>❌ Resume crashes with "empty string path" error</b></summary>
<br/>

Always include `--app` and `--output` when resuming. The checkpoint only stores chunk progress — not the original command:

```batch
LustsDepotDownloaderPro.exe --app 730 --output "C:\Games\CSGO" ^
  --resume "C:\Games\CSGO\730_CSGO\checkpoint_730.json"
```

</details>

---

## 🗺️ Roadmap

- [ ] Bandwidth / speed limiting
- [ ] BitTorrent protocol support
- [ ] Delta patching for incremental updates
- [ ] Mirror server support
- [ ] Web UI
- [ ] Docker container
- [ ] Auto-update mechanism
- [ ] Download scheduling & queuing
- [ ] Steam Workshop collection batch downloads

---

<div align="center">

<img src="https://capsule-render.vercel.app/api?type=waving&color=gradient&customColorList=6,11,20&height=120&section=footer" width="100%"/>

**Made with ❤️ by The Lust — v1.0.0 — March 2026**

<sub>Hat tip to <a href="https://github.com/oureveryday">oureveryday</a> for the foundational depot downloader work that made this possible.</sub>

</div>
