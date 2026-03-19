@echo off
echo ========================================
echo  Linux x64 ^| RELEASE
echo ========================================
echo.
dotnet --version >nul 2>&1
if errorlevel 1 ( echo ERROR: .NET 8 SDK not found. & pause & exit /b 1 )
dotnet restore
dotnet publish -c Release -r linux-x64 --self-contained true ^
    /p:PublishSingleFile=true ^
    /p:IncludeNativeLibrariesForSelfExtract=true ^
    /p:PublishReadyToRun=true ^
    /p:DebugType=none ^
    /p:DebugSymbols=false ^
    -o publish\linux-x64-release
if exist publish\linux-x64-release\LustsDepotDownloaderPro (
    echo.
    echo [OK] publish\linux-x64-release\LustsDepotDownloaderPro
    for %%A in (publish\linux-x64-release\LustsDepotDownloaderPro) do echo [OK] Size: %%~zA bytes
) else ( echo [FAIL] Build failed. & pause & exit /b 1 )
echo.
pause
