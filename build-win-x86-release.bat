@echo off
echo ========================================
echo  Win x86 (32-bit) ^| RELEASE
echo ========================================
echo.
dotnet --version >nul 2>&1
if errorlevel 1 ( echo ERROR: .NET 8 SDK not found. & pause & exit /b 1 )
dotnet restore
dotnet publish -c Release -r win-x86 --self-contained true ^
    /p:PublishSingleFile=true ^
    /p:EnableCompressionInSingleFile=true ^
    /p:IncludeNativeLibrariesForSelfExtract=true ^
    /p:PublishReadyToRun=true ^
    /p:DebugType=none ^
    /p:DebugSymbols=false ^
    -o publish\win-x86-release
if exist publish\win-x86-release\LustsDepotDownloaderPro.exe (
    echo.
    echo [OK] publish\win-x86-release\LustsDepotDownloaderPro.exe
    for %%A in (publish\win-x86-release\LustsDepotDownloaderPro.exe) do echo [OK] Size: %%~zA bytes
) else ( echo [FAIL] Build failed. & pause & exit /b 1 )
echo.
pause
