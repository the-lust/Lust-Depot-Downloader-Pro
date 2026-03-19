@echo off
echo ========================================
echo  macOS arm64 (M-series) ^| RELEASE
echo ========================================
echo.
dotnet --version >nul 2>&1
if errorlevel 1 ( echo ERROR: .NET 8 SDK not found. & pause & exit /b 1 )
dotnet restore
dotnet publish -c Release -r osx-arm64 --self-contained true ^
    /p:PublishSingleFile=true ^
    /p:IncludeNativeLibrariesForSelfExtract=true ^
    /p:PublishReadyToRun=true ^
    /p:DebugType=none ^
    /p:DebugSymbols=false ^
    -o publish\osx-arm64-release
if exist publish\osx-arm64-release\LustsDepotDownloaderPro (
    echo.
    echo [OK] publish\osx-arm64-release\LustsDepotDownloaderPro
    for %%A in (publish\osx-arm64-release\LustsDepotDownloaderPro) do echo [OK] Size: %%~zA bytes
) else ( echo [FAIL] Build failed. & pause & exit /b 1 )
echo.
pause
