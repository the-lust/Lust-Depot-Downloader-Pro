#!/bin/bash
# Lusts Depot Downloader Pro - build.sh
# Builds a self-contained release binary for the current OS/arch.
# Run with: ./build.sh
# Or cross-compile: ./build.sh linux-x64 | osx-x64 | osx-arm64

set -e

echo ""
echo " ============================================================"
echo "  Lusts Depot Downloader Pro - Build"
echo " ============================================================"
echo ""

# ── Check .NET SDK ────────────────────────────────────────────────
if ! command -v dotnet &>/dev/null; then
    echo "ERROR: .NET 8 SDK not found."
    echo "Install: https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
fi

# ── Detect or accept runtime ──────────────────────────────────────
if [ -n "$1" ]; then
    RUNTIME="$1"
else
    if [[ "$OSTYPE" == "linux-gnu"* ]]; then
        ARCH=$(uname -m)
        [ "$ARCH" = "aarch64" ] && RUNTIME="linux-arm64" || RUNTIME="linux-x64"
    elif [[ "$OSTYPE" == "darwin"* ]]; then
        ARCH=$(uname -m)
        [ "$ARCH" = "arm64" ] && RUNTIME="osx-arm64" || RUNTIME="osx-x64"
    else
        echo "ERROR: Unknown OS. Pass runtime manually: ./build.sh linux-x64"
        exit 1
    fi
fi

echo "[1/4] Target runtime : $RUNTIME"
echo "[2/4] Cleaning old builds..."
rm -rf bin obj "publish/$RUNTIME"

echo "[3/4] Restoring packages..."
dotnet restore

echo "[4/4] Publishing..."
dotnet publish -c Release -r "$RUNTIME" --self-contained true \
    /p:PublishSingleFile=true \
    /p:IncludeNativeLibrariesForSelfExtract=true \
    /p:PublishReadyToRun=true \
    /p:DebugType=none \
    /p:DebugSymbols=false \
    -o "publish/$RUNTIME"

# Clean any stray PDB files
find "publish/$RUNTIME" -name "*.pdb" -delete 2>/dev/null || true

BIN="publish/$RUNTIME/LustsDepotDownloaderPro"

if [ -f "$BIN" ]; then
    chmod +x "$BIN"
    SIZE=$(du -sh "$BIN" | cut -f1)
    echo ""
    echo " ============================================================"
    echo "  BUILD SUCCESSFUL"
    echo " ============================================================"
    echo ""
    echo "  Output : $BIN"
    echo "  Size   : $SIZE"
    echo ""
    echo "  Run it :"
    echo "    ./$BIN --help"
    echo "    ./$BIN --app 730 --output ~/Games --max-downloads 32"
    echo ""
else
    echo ""
    echo " ============================================================"
    echo "  BUILD FAILED"
    echo " ============================================================"
    exit 1
fi
