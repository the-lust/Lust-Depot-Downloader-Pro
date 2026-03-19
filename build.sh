#!/bin/bash
# Build script for Lusts Depot Downloader Pro (Linux/macOS)
# Builds a single self-contained executable

echo "========================================"
echo "Lusts Depot Downloader Pro - Build"
echo "========================================"
echo ""

# Check if .NET 8 SDK is installed
if ! command -v dotnet &> /dev/null; then
    echo "ERROR: .NET 8 SDK is not installed"
    echo "Please install from: https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
fi

echo "[1/4] Cleaning previous builds..."
rm -rf bin obj publish

echo "[2/4] Restoring NuGet packages..."
dotnet restore

echo "[3/4] Building Release version..."
dotnet build -c Release

echo "[4/4] Publishing self-contained executable..."

# Detect OS
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    RUNTIME="linux-x64"
elif [[ "$OSTYPE" == "darwin"* ]]; then
    RUNTIME="osx-x64"
else
    echo "Unsupported OS: $OSTYPE"
    exit 1
fi

dotnet publish -c Release -r $RUNTIME --self-contained true \
    /p:PublishSingleFile=true \
    /p:IncludeNativeLibrariesForSelfExtract=true \
    /p:PublishReadyToRun=true \
    -o publish/$RUNTIME

if [ -f "publish/$RUNTIME/LustsDepotDownloaderPro" ]; then
    chmod +x "publish/$RUNTIME/LustsDepotDownloaderPro"
    
    echo ""
    echo "========================================"
    echo "BUILD SUCCESSFUL!"
    echo "========================================"
    echo ""
    echo "Executable location:"
    echo "  publish/$RUNTIME/LustsDepotDownloaderPro"
    echo ""
    echo "File size:"
    ls -lh "publish/$RUNTIME/LustsDepotDownloaderPro" | awk '{print "  " $5}'
    echo ""
    echo "You can now run:"
    echo "  ./publish/$RUNTIME/LustsDepotDownloaderPro --help"
    echo ""
else
    echo ""
    echo "========================================"
    echo "BUILD FAILED!"
    echo "========================================"
    echo "Check the error messages above"
    exit 1
fi
