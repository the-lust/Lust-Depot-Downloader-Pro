@echo off
REM Build script for Lusts Depot Downloader Pro (Windows)
REM Builds a single self-contained executable

echo ========================================
echo Lusts Depot Downloader Pro - Build
echo ========================================
echo.

REM Check if .NET 8 SDK is installed
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET 8 SDK is not installed
    echo Please install from: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo [1/4] Cleaning previous builds...
if exist bin rd /s /q bin
if exist obj rd /s /q obj
if exist publish rd /s /q publish

echo [2/4] Restoring NuGet packages...
dotnet restore

echo [3/4] Building Release version...
dotnet build -c Release

echo [4/4] Publishing self-contained executable...
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishReadyToRun=true -o publish/win-x64

if exist publish\win-x64\LustsDepotDownloaderPro.exe (
    echo.
    echo ========================================
    echo BUILD SUCCESSFUL!
    echo ========================================
    echo.
    echo Executable location:
    echo   publish\win-x64\LustsDepotDownloaderPro.exe
    echo.
    echo File size:
    for %%A in (publish\win-x64\LustsDepotDownloaderPro.exe) do echo   %%~zA bytes
    echo.
    echo You can now run:
    echo   publish\win-x64\LustsDepotDownloaderPro.exe --help
    echo.
) else (
    echo.
    echo ========================================
    echo BUILD FAILED!
    echo ========================================
    echo Check the error messages above
    pause
    exit /b 1
)

pause
