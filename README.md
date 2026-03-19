<div align="center">

# 🔥 Lusts Depot Downloader Pro

**A Steam depot downloader — but with extra fun.**

[![License: GPL-2.0](https://img.shields.io/badge/License-GPL%202.0-blue.svg)](LICENSE)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey.svg)]()
[![Version](https://img.shields.io/badge/Version-1.0.0-green.svg)]()

*Download Steam depots fast, resumably, and without the headache.*

</div>

---

## 📌 What Is This?

**Lusts Depot Downloader Pro** is a command-line Steam depot downloader built on top of [SteamKit2](https://github.com/SteamRE/SteamKit). It grabs game depots directly from Steam's CDN — anonymously or with an account — with multi-threaded downloading, pause/resume support, CDN failover, and a clean terminal UI.

It's essentially a supercharged depot downloader with community manifest support, workshop item downloading, checkpoint resuming, and a bunch of quality-of-life extras baked in. Nothing more, nothing less.

> ⚠️ **For educational purposes and backup of owned content only. Always comply with Steam's Terms of Service.**

---

## ✨ Features

### Core
| Feature | Details |
|---|---|
| 🚀 **Multi-threaded** | 1–64 concurrent download workers |
| ⏸️ **Pause & Resume** | Checkpoint system — pick up exactly where you left off |
| 🌐 **CDN Failover** | Automatic fallback across 20+ Steam CDN servers |
| 📄 **Manifest Support** | Local manifest files + community manifest sources |
| 🔑 **Depot Keys** | Load keys from file for encrypted depots |
| 🎯 **Workshop Items** | Download via PublishedFileId or UGC ID |
| 🌿 **Branch Support** | Public, beta, or any custom branch |
| 🔍 **File Filtering** | Wildcard and regex filters — download only what you need |
| ✅ **Checksum Validation** | Verify file integrity after download |
| 🎨 **Terminal UI** | Live progress bars, speed, ETA — powered by Spectre.Console |
| 📦 **Single Executable** | Self-contained, no installs, no dependencies |
| 🖥️ **Cross-Platform** | Windows, Linux, macOS |

### Advanced
| Feature | Details |
|---|---|
| 🔐 **Authentication** | Anonymous or full login with Steam Guard / 2FA / QR code |
| 🎯 **Platform Filtering** | OS and architecture-specific depot selection |
| 🌍 **Language Selection** | Download only your language's depot |
| 📊 **Real-time Stats** | Live MB/s speed, percentage, and ETA |
| 🔄 **Auto Retry** | Exponential backoff on chunk failures |
| 💾 **Low Memory Mode** | Lazy chunk scheduling for massive games |
| 🐍 **Python Scripts** | Manifest and key generation from community sources |
| 🔧 **GUI-Ready CLI** | Parse-friendly output for wrapping in GUI apps |

---

## 🛠️ Installation

### Option 1 — Pre-built Executable *(Recommended)*
1. Download the latest release
2. Extract `LustsDepotDownloaderPro.exe` (Windows) or `LustsDepotDownloaderPro` (Linux/macOS)
3. Run from your terminal

### Option 2 — Build from Source

**Prerequisites:**
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Python 3.8+ *(optional, for manifest scripts)*

**Windows:**
```batch
cd LustsDepotDownloaderPro
build.bat
# Output: publish\win-x64\LustsDepotDownloaderPro.exe
```

**Linux / macOS:**
```bash
cd LustsDepotDownloaderPro
chmod +x build.sh
./build.sh
# Output: publish/linux-x64/LustsDepotDownloaderPro
```

**Manual:**
```bash
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish/win-x64
```

---

## 📖 Usage

### The Basics

```bash
# Download a game anonymously
LustsDepotDownloaderPro --app 730 --output "C:\Games\CSGO"

# Download with your Steam account
LustsDepotDownloaderPro --app 730 --username myuser --password mypass --output "C:\Games\CSGO"

# Download a specific depot + manifest
LustsDepotDownloaderPro --app 730 --depot 731 --manifest 7617088375292372759 \
  --depot-keys depot_keys.txt --output "C:\Games\CSGO"

# Download a workshop item
LustsDepotDownloaderPro --app 730 --pubfile 1885082371 --output "C:\Games\Workshop"
```

### Pause & Resume

Press **Ctrl+C** once to pause gracefully — a checkpoint file is saved automatically.

```bash
# Resume exactly where you left off
LustsDepotDownloaderPro --app 730 --output "C:\Games\CSGO" \
  --resume "C:\Games\CSGO\730_GameName\checkpoint_730.json"
```

> 💡 The checkpoint path is always `<output>\<appid>_<name>\checkpoint_<appid>.json`

### High-Speed Downloading

Workers are parallel download threads. More workers = faster downloads (up to your connection's limit).

```bash
# Sweet spot for most connections
LustsDepotDownloaderPro --app 730 --output "C:\Games\CSGO" --max-downloads 32

# Maximum (fast connections only — may cause CDN rate limiting above 32)
LustsDepotDownloaderPro --app 730 --output "C:\Games\CSGO" --max-downloads 64
```

| Workers | Best For |
|---|---|
| 4–8 | Slow/shared connections |
| 16–32 | Most home connections ✅ Sweet spot |
| 64 | Very fast connections (may trigger CDN rate limits) |

### File Filtering

Create a `filelist.txt`:
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

```bash
LustsDepotDownloaderPro --app 730 --filelist filelist.txt --output "C:\Games\CSGO"
```

### Other Common Uses

```bash
# Download from beta branch
LustsDepotDownloaderPro --app 730 --branch beta --output "C:\Games\CSGO-Beta"

# Download all platforms and all languages
LustsDepotDownloaderPro --app 730 --all-platforms --all-languages --output "C:\Games\CSGO"

# Validate checksums after download
LustsDepotDownloaderPro --app 730 --output "C:\Games\CSGO" --validate

# Enable debug logging
LustsDepotDownloaderPro --app 730 --output "C:\Games\CSGO" --debug
```

---

## 🎛️ All Command Line Options

### Essential

| Option | Short | Description | Example |
|--------|-------|-------------|---------|
| `--app` | `-a` | AppID to download | `--app 730` |
| `--depot` | `-d` | Specific DepotID (downloads all if omitted) | `--depot 731` |
| `--manifest` | `-m` | Specific Manifest ID | `--manifest 7617088375292372759` |
| `--output` | `-o` | Output directory | `--output "C:\Games"` |

### Authentication

| Option | Short | Description | Example |
|--------|-------|-------------|---------|
| `--username` | `-u` | Steam username | `--username myuser` |
| `--password` | `-p` | Steam password | `--password mypass` |
| `--qr` | | QR code login (Steam mobile app) | `--qr` |
| `--remember-password` | `-rp` | Save credentials for next time | `--remember-password` |

### Depot & Manifest

| Option | Short | Description | Example |
|--------|-------|-------------|---------|
| `--depot-keys` | `-dk` | Depot keys file | `--depot-keys keys.txt` |
| `--manifest-file` | `-mf` | Local manifest file | `--manifest-file game.manifest` |
| `--app-token` | `-at` | App access token | `--app-token 1234567890` |
| `--branch` | `-b` | Branch name | `--branch beta` |
| `--branch-password` | `-bp` | Branch password | `--branch-password secret` |

### Download Control

| Option | Short | Description | Example |
|--------|-------|-------------|---------|
| `--max-downloads` | `-md` | Concurrent workers (1–64) | `--max-downloads 32` |
| `--resume` | `-r` | Resume from checkpoint file | `--resume checkpoint.json` |
| `--validate` | `-v` | Verify checksums after download | `--validate` |
| `--pause` | | Pause active download | `--pause` |
| `--status` | `-s` | Show current download status | `--status` |

### Filtering

| Option | Short | Description | Example |
|--------|-------|-------------|---------|
| `--filelist` | `-fl` | File filter list | `--filelist filters.txt` |
| `--os` | | Target OS (`windows`/`macos`/`linux`) | `--os windows` |
| `--os-arch` | `-arch` | Architecture (`32`/`64`) | `--os-arch 64` |
| `--language` | `-lang` | Language | `--language english` |
| `--all-platforms` | `-ap` | Download all platform depots | `--all-platforms` |
| `--all-languages` | `-al` | Download all language depots | `--all-languages` |

### Workshop

| Option | Short | Description | Example |
|--------|-------|-------------|---------|
| `--pubfile` | `-pf` | PublishedFileId | `--pubfile 1885082371` |
| `--ugc` | | UGC ID | `--ugc 770604181014286929` |

### Misc

| Option | Short | Description |
|--------|-------|-------------|
| `--debug` | | Verbose debug logging |
| `--terminal-ui` | `-tui` | Enable live terminal UI (default: on) |
| `--cellid` | `-c` | Override CDN cell ID |
| `--loginid` | `-lid` | Steam Login ID (for running multiple instances) |
| `--api-key` | `-key` | GitHub API key for community manifest sources |

---

## 🐍 Python Scripts

Generate depot keys and manifests from community sources before downloading.

```bash
cd Scripts
pip install -r requirements.txt

# Generate everything for an app
python generate_manifests.py 730

# Keys only
python generate_manifests.py 730 --keys-only

# Generate a ready-to-run batch file
python generate_manifests.py 730 --batch

# List available manifests
python generate_manifests.py 730 --list-manifests

# Custom output folder
python generate_manifests.py 730 --output my_manifests
```

**Output files:**
- `depot_keys_<appid>.txt` — depot keys in `depotID;hexKey` format
- `manifests_<appid>.json` — available manifest IDs
- `download_<appid>.bat` — ready-to-run download batch file

---

## 📋 File Formats

### Depot Keys (`depot_keys.txt`)
```
# Format: depotID;hexKey
731;E5A1D6C2F8B3A4E9D7C1F2A8B4C6E3D9
732;A4B9E3F7C2D8A1E6B3F9C4D7E2A8B1C6
```

### Checkpoint File *(auto-generated, don't touch)*
```json
{
  "CompletedChunks": ["abc123...", "def456..."],
  "LastSaved": "2026-03-17T10:30:00Z"
}
```

---

## 📁 Project Structure

```
LustsDepotDownloaderPro/
├── Core/
│   ├── DownloadEngine.cs          # Main orchestration
│   ├── DownloadWorker.cs          # Per-worker download logic
│   ├── ChunkScheduler.cs          # Chunk queue management
│   ├── GlobalProgress.cs          # Progress tracking
│   ├── Checkpoint.cs              # Pause/resume state
│   ├── FileAssembler.cs           # Thread-safe file writing
│   └── DownloadSessionBuilder.cs  # Session setup
├── Steam/
│   ├── SteamSession.cs            # Auth & Steam connection
│   └── ManifestParser.cs          # Manifest decoding
├── Models/
│   ├── DownloadOptions.cs         # CLI option model
│   └── DownloadSession.cs         # Session state model
├── Utils/
│   ├── Logger.cs                  # Logging
│   ├── FileUtils.cs               # File helpers
│   └── VZipDecompressor.cs        # Steam VZip decompression
├── UI/
│   └── TerminalUI.cs              # Spectre.Console UI
├── Scripts/
│   └── generate_manifests.py      # Python manifest/key generator
├── Program.cs
├── LustsDepotDownloaderPro.csproj
├── build.bat
└── build.sh
```

---

## 🔧 GUI Integration

The tool is designed to be wrapped by GUI apps. Parse its stdout for progress updates.

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

process.OutputDataReceived += (s, e) =>
{
    // Log format: [HH:mm:ss.fff] [LEVEL] Message
    UpdateProgressBar(e.Data);
};

process.Start();
process.BeginOutputReadLine();
await process.WaitForExitAsync();
```

**Exit codes:**
- `0` — Success
- `1` — Error
- `2` — Paused (checkpoint saved)

---

## 🚨 Troubleshooting

### "Failed to connect to Steam"
- Check your internet connection
- Try a different CDN cell with `--cellid`
- Check firewall / antivirus isn't blocking outbound connections

### "Failed to get depot key" / AccessDenied
- Use `--depot-keys` with a keys file
- Some depots require account ownership — try `--username`
- Generate keys with the Python script

### "Manifest not found"
- Double-check your AppID and DepotID
- Try `--branch beta` or another branch
- Use `python generate_manifests.py <appid> --list-manifests`

### "All CDN servers failed"
- Lower `--max-downloads` — too many workers can trigger rate limiting
- Check if the game is region-locked

### Download is slow
- Increase workers: `--max-downloads 32`
- Default is 8 — most connections can handle 16–32 comfortably

### "file is being used by another process" warnings
- These are harmless — multiple workers briefly contend on the same large file
- The worker retries automatically and the file is written correctly

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

*Made with ❤️ by the community — Version 2.0.0 — March 2026*

*Big thanks to [oureveryday](https://github.com/oureveryday) for the original DepotDownloader work that made this possible.*

</div>
