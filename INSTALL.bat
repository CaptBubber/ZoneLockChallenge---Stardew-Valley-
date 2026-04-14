@echo off
setlocal enabledelayedexpansion
title Zone Lock Challenge - Installer
color 0B

echo.
echo  =============================================
echo     ZONE LOCK CHALLENGE - Auto Installer
echo  =============================================
echo.

:: ── Step 1: Check for SMAPI / Find game folder ─────────────────
echo [1/4] Looking for Stardew Valley...

set "GAMEPATH="

:: Check common Steam paths
for %%P in (
    "C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley"
    "C:\Program Files\Steam\steamapps\common\Stardew Valley"
    "D:\Steam\steamapps\common\Stardew Valley"
    "D:\SteamLibrary\steamapps\common\Stardew Valley"
    "E:\Steam\steamapps\common\Stardew Valley"
    "E:\SteamLibrary\steamapps\common\Stardew Valley"
    "D:\Games\Steam\steamapps\common\Stardew Valley"
    "D:\Games\SteamLibrary\steamapps\common\Stardew Valley"
) do (
    if exist "%%~P\Stardew Valley.dll" (
        set "GAMEPATH=%%~P"
        goto :found_game
    )
)

:: Try to find via Steam's libraryfolders.vdf
for %%S in (
    "C:\Program Files (x86)\Steam"
    "C:\Program Files\Steam"
    "D:\Steam"
) do (
    if exist "%%~S\steamapps\libraryfolders.vdf" (
        for /f "tokens=2 delims=	 " %%L in ('findstr /C:"path" "%%~S\steamapps\libraryfolders.vdf" 2^>nul') do (
            set "LIBPATH=%%~L"
            set "LIBPATH=!LIBPATH:\\=\!"
            if exist "!LIBPATH!\steamapps\common\Stardew Valley\Stardew Valley.dll" (
                set "GAMEPATH=!LIBPATH!\steamapps\common\Stardew Valley"
                goto :found_game
            )
        )
    )
)

echo.
echo  [ERROR] Could not find Stardew Valley automatically.
echo.
echo  Please drag your Stardew Valley folder onto this window
echo  (or type/paste the full path) and press Enter:
echo.
echo  Example: C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley
echo.
set /p "GAMEPATH=Path: "

:: Remove surrounding quotes if present
set "GAMEPATH=!GAMEPATH:"=!"

if not exist "!GAMEPATH!\Stardew Valley.dll" (
    echo.
    echo  [ERROR] That folder doesn't contain Stardew Valley.dll
    echo  Make sure you pointed to the correct folder.
    goto :error_exit
)

:found_game
echo  Found: !GAMEPATH!

:: Check SMAPI is installed
if not exist "!GAMEPATH!\StardewModdingAPI.dll" (
    echo.
    echo  [ERROR] SMAPI is not installed!
    echo.
    echo  You need to install SMAPI first:
    echo    1. Go to https://smapi.io
    echo    2. Download and run the installer
    echo    3. Then run this script again
    echo.
    goto :error_exit
)
echo  SMAPI detected.

:: ── Step 2: Check for .NET SDK ──────────────────────────────────
echo.
echo [2/4] Checking for .NET SDK...

where dotnet >nul 2>&1
if errorlevel 1 (
    echo  .NET SDK not found. Installing...
    echo.
    
    :: Download .NET 8.0 SDK installer
    echo  Downloading .NET 8.0 SDK...
    powershell -Command "& {[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile '%TEMP%\dotnet-install.ps1'}" 2>nul

    if exist "%TEMP%\dotnet-install.ps1" (
        echo  Installing .NET 8.0 SDK (this may take a few minutes)...
        powershell -ExecutionPolicy Bypass -File "%TEMP%\dotnet-install.ps1" -Channel 8.0 -InstallDir "%USERPROFILE%\.dotnet" 2>nul
        set "PATH=%USERPROFILE%\.dotnet;%PATH%"
        
        where dotnet >nul 2>&1
        if errorlevel 1 (
            set "DOTNET=%USERPROFILE%\.dotnet\dotnet.exe"
        ) else (
            set "DOTNET=dotnet"
        )
    ) else (
        echo.
        echo  [ERROR] Could not download .NET installer.
        echo  Please install .NET 8.0 SDK manually from:
        echo  https://dotnet.microsoft.com/download/dotnet/8.0
        echo  Then run this script again.
        goto :error_exit
    )
) else (
    set "DOTNET=dotnet"
)

:: Verify dotnet works
"!DOTNET!" --version >nul 2>&1
if errorlevel 1 (
    :: Fallback: check user profile install
    if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
        set "DOTNET=%USERPROFILE%\.dotnet\dotnet.exe"
    ) else (
        echo  [ERROR] .NET SDK installation failed.
        echo  Please install manually from: https://dotnet.microsoft.com/download/dotnet/8.0
        goto :error_exit
    )
)

for /f "tokens=*" %%V in ('"!DOTNET!" --version 2^>nul') do echo  .NET SDK version: %%V

:: ── Step 3: Build the mod ───────────────────────────────────────
echo.
echo [3/4] Building Zone Lock Challenge...

:: Get the directory this script is in
set "MODPROJECT=%~dp0"

:: Set GamePath for ModBuildConfig
set "GamePath=!GAMEPATH!"

:: Create a temporary Directory.Build.targets to pass GamePath
echo ^<Project^>^<PropertyGroup^>^<GamePath^>!GAMEPATH!^</GamePath^>^</PropertyGroup^>^</Project^> > "!MODPROJECT!Directory.Build.targets"

:: Build
"!DOTNET!" build "!MODPROJECT!ZoneLockChallenge.csproj" --configuration Release 2>&1

if errorlevel 1 (
    echo.
    echo  [ERROR] Build failed. See errors above.
    echo  Common fixes:
    echo    - Make sure SMAPI is installed
    echo    - Make sure .NET 8.0 SDK is installed
    echo    - Try running this script as Administrator
    del /q "!MODPROJECT!Directory.Build.targets" 2>nul
    goto :error_exit
)

:: Clean up temp file
del /q "!MODPROJECT!Directory.Build.targets" 2>nul

echo  Build successful!

:: ── Step 4: Deploy to Mods folder ───────────────────────────────
echo.
echo [4/4] Installing mod...

set "MODSDIR=!GAMEPATH!\Mods\ZoneLockChallenge"
if not exist "!MODSDIR!" mkdir "!MODSDIR!"

:: Copy built files
set "BUILDOUT=!MODPROJECT!bin\Release\net8.0"

if not exist "!BUILDOUT!\ZoneLockChallenge.dll" (
    echo  [ERROR] Build output not found at: !BUILDOUT!
    echo  Trying to find it...
    
    :: ModBuildConfig might have deployed it directly
    if exist "!MODSDIR!\ZoneLockChallenge.dll" (
        echo  Mod was auto-deployed by SMAPI build tools!
        goto :success
    )
    goto :error_exit
)

copy /y "!BUILDOUT!\ZoneLockChallenge.dll" "!MODSDIR!\" >nul
copy /y "!BUILDOUT!\ZoneLockChallenge.pdb" "!MODSDIR!\" >nul 2>nul
copy /y "!MODPROJECT!manifest.json" "!MODSDIR!\" >nul

:: Copy config if it doesn't already exist (don't overwrite custom config)
if not exist "!MODSDIR!\config.json" (
    echo  (First install - default config will be generated on first run)
)

:success
echo.
echo  =============================================
echo     INSTALLATION COMPLETE!
echo  =============================================
echo.
echo  Mod installed to:
echo    !MODSDIR!
echo.
echo  Next steps:
echo    1. Launch Stardew Valley from Steam
echo    2. Check the SMAPI console for:
echo       "Zone Lock Challenge loaded"
echo    3. In-game, press K to open the Zone Board
echo.
echo  Have fun!
echo.
pause
exit /b 0

:error_exit
echo.
echo  Installation was not completed.
echo  Fix the error above and try again.
echo.
pause
exit /b 1
