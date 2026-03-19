@echo off
echo ========================================
echo  macOS x64 (Intel) ^| RELEASE
echo ========================================
echo.
dotnet --version >nul 2>&1
if errorlevel 1 ( echo ERROR: .NET 8 SDK not found. & pause & exit /b 1 )
dotnet restore
dotnet publish -c Release -r osx-x64 --self-contained true ^
    /p:PublishSingleFile=true ^
    /p:IncludeNativeLibrariesForSelfExtract=true ^
    /p:PublishReadyToRun=true ^
    /p:DebugType=none ^
    /p:DebugSymbols=false ^
    -o publish\osx-x64-release
if exist publish\osx-x64-release\LustsDepotDownloaderPro (
    echo.
    echo [OK] publish\osx-x64-release\LustsDepotDownloaderPro
    for %%A in (publish\osx-x64-release\LustsDepotDownloaderPro) do echo [OK] Size: %%~zA bytes
) else ( echo [FAIL] Build failed. & pause & exit /b 1 )
echo.
pause
