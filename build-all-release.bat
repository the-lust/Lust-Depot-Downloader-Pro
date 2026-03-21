@echo off
echo.
echo  ============================================================
echo   Lusts Depot Downloader Pro - Build ALL Platforms (Release)
echo  ============================================================
echo.

dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET 8 SDK not found.
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    pause & exit /b 1
)

echo [1/5] Cleaning previous builds...
if exist publish rd /s /q publish

echo [2/5] Restoring NuGet packages...
dotnet restore
if errorlevel 1 ( echo ERROR: Restore failed. & pause & exit /b 1 )

echo.
echo [3/5] Building all platforms...
echo.

set FAILED=0
set FLAGS=/p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishReadyToRun=true /p:DebugType=none /p:DebugSymbols=false

:: Windows x64
echo   ^> win-x64...
dotnet publish -c Release -r win-x64 --self-contained true %FLAGS% -o publish\win-x64 >nul 2>&1
if exist publish\win-x64\LustsDepotDownloaderPro.exe ( echo   [OK] win-x64 ) else ( echo   [FAIL] win-x64 & set FAILED=1 )

:: Windows x86
echo   ^> win-x86...
dotnet publish -c Release -r win-x86 --self-contained true %FLAGS% -o publish\win-x86 >nul 2>&1
if exist publish\win-x86\LustsDepotDownloaderPro.exe ( echo   [OK] win-x86 ) else ( echo   [FAIL] win-x86 & set FAILED=1 )

:: Linux x64
echo   ^> linux-x64...
dotnet publish -c Release -r linux-x64 --self-contained true %FLAGS% -o publish\linux-x64 >nul 2>&1
if exist publish\linux-x64\LustsDepotDownloaderPro ( echo   [OK] linux-x64 ) else ( echo   [FAIL] linux-x64 & set FAILED=1 )

:: macOS x64
echo   ^> osx-x64...
dotnet publish -c Release -r osx-x64 --self-contained true %FLAGS% -o publish\osx-x64 >nul 2>&1
if exist publish\osx-x64\LustsDepotDownloaderPro ( echo   [OK] osx-x64 ) else ( echo   [FAIL] osx-x64 & set FAILED=1 )

:: macOS arm64
echo   ^> osx-arm64...
dotnet publish -c Release -r osx-arm64 --self-contained true %FLAGS% -o publish\osx-arm64 >nul 2>&1
if exist publish\osx-arm64\LustsDepotDownloaderPro ( echo   [OK] osx-arm64 ) else ( echo   [FAIL] osx-arm64 & set FAILED=1 )

echo.
echo [4/5] Cleaning up PDB files (just in case)...
for /r publish %%f in (*.pdb) do del /q "%%f"

echo [5/5] Done.
echo.

if %FAILED%==1 (
    echo  ============================================================
    echo   SOME BUILDS FAILED — check output above
    echo  ============================================================
) else (
    echo  ============================================================
    echo   ALL BUILDS SUCCESSFUL
    echo  ============================================================
    echo.
    echo   publish\win-x64\LustsDepotDownloaderPro.exe
    echo   publish\win-x86\LustsDepotDownloaderPro.exe
    echo   publish\linux-x64\LustsDepotDownloaderPro
    echo   publish\osx-x64\LustsDepotDownloaderPro
    echo   publish\osx-arm64\LustsDepotDownloaderPro
)

echo.
pause
