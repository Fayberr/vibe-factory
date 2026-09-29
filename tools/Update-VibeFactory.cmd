@echo off
rem Vibe Factory updater. Put this file in the folder you play the game from and
rem double-click it: it fetches the newest release, replaces everything in this
rem folder except this script, and starts nothing. Your saves are not here (they
rem live in Godot's user folder), so they are never touched.
rem Revision 4 (2026-09-29): no question at the start, and the window closes by
rem itself after a clean update. It stops and waits only when something is wrong.
setlocal EnableExtensions
title Vibe Factory updater
pushd "%~dp0" || (echo Could not open the folder this script is in. & pause & exit /b 1)

set "URL=https://github.com/Fayberr/vibe-factory/releases/latest/download/VibeFactory-Windows.zip"
set "ZIP=%TEMP%\VibeFactory-Windows.zip"
set "MYSELF=%~nx0"
set "HERE=%CD%"
rem Set only when something went wrong. A clean run closes the window by itself.
set "FAILED="

echo Vibe Factory updater
echo Folder: %HERE%
echo.

rem The game cannot be replaced while it holds its own files open.
tasklist /fi "imagename eq VibeFactory.exe" 2>nul | find /i "VibeFactory.exe" >nul
if not errorlevel 1 (
    echo VibeFactory.exe is running. Close the game and run this again.
    echo Nothing was changed.
    set "FAILED=1"
    goto :done
)

rem No question here on purpose, this runs unattended. Instead of asking it simply
rem refuses: any one of the game's own files is enough to go ahead, and a folder
rem without one is not a play folder, so a double-click in the wrong place cannot
rem delete anything. Tell it where the game is, or unzip a copy in here first.
set "GAMEFOLDER="
if exist "%HERE%\VibeFactory.exe" set "GAMEFOLDER=1"
if exist "%HERE%\VibeFactory.pck" set "GAMEFOLDER=1"
dir /b /ad "%HERE%\data_*" >nul 2>nul
if not errorlevel 1 set "GAMEFOLDER=1"
if not defined GAMEFOLDER (
    echo This folder does not look like a Vibe Factory folder. It has no
    echo VibeFactory.exe, no VibeFactory.pck and no data_ folder in it.
    echo Nothing was changed. Put this script next to the game, or unzip a
    echo copy of the release in here and run this again.
    set "FAILED=1"
    goto :done
)

:download
rem Download before deleting anything, so a failure leaves the old build alone.
echo Downloading the newest build...
if exist "%ZIP%" del /q "%ZIP%"
where curl.exe >nul 2>nul
if errorlevel 1 goto :download_ps

rem The release host here gives either about 15 MB/s or about 0.5 MB/s, which is
rem the difference between six seconds and three minutes, and which one you get is
rem decided per transfer, not per address: all four of its IPv4 addresses behave
rem the same and it has no IPv6 at all. So a slow transfer is treated as a failed
rem one and drawn again. The last attempt takes whatever it can get, so a line
rem that is genuinely congested still ends with a working build, just a slow one.
set "TRY=0"
:trydownload
set /a TRY+=1
if %TRY% GTR 3 goto :lasttry
curl.exe -L -f --retry 2 --speed-limit 2000000 --speed-time 6 -o "%ZIP%" "%URL%"
if not errorlevel 1 goto :downloaded
rem A slow or failed attempt leaves a partial file behind. The next step deletes
rem everything else in this folder, so a partial file must never be mistaken for
rem the build.
if exist "%ZIP%" del /q "%ZIP%"
echo That attempt was far too slow to wait for. Trying again.
goto :trydownload

:lasttry
echo Three slow attempts in a row. Taking this one however long it takes.
curl.exe -L -f --retry 3 -o "%ZIP%" "%URL%"
if errorlevel 1 if exist "%ZIP%" del /q "%ZIP%"
goto :downloaded

:download_ps
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -Uri '%URL%' -OutFile '%ZIP%'"

:downloaded
if not exist "%ZIP%" (
    echo.
    echo Download failed. Nothing was changed, the build in this folder still works.
    set "FAILED=1"
    goto :done
)
set "SIZE=0"
for %%A in ("%ZIP%") do set "SIZE=%%~zA"
rem 87 MB today. Anything under 20 MB is a truncated download or an error page.
if %SIZE% LSS 20000000 (
    echo.
    echo That download is too small to be the game. Nothing was changed.
    del /q "%ZIP%"
    set "FAILED=1"
    goto :done
)

rem Prove the archive can be opened and really contains the game before a single
rem file in this folder is deleted. A download that fails this costs nothing.
powershell -NoProfile -Command "try { Add-Type -AssemblyName System.IO.Compression.FileSystem; $z = [IO.Compression.ZipFile]::OpenRead('%ZIP%'); $e = $z.Entries | Where-Object { $_.FullName -eq 'VibeFactory.exe' }; $z.Dispose(); if ($e) { exit 0 } else { exit 1 } } catch { exit 1 }"
if errorlevel 1 (
    echo.
    echo That download is not a readable game archive. Nothing was changed.
    del /q "%ZIP%"
    set "FAILED=1"
    goto :done
)

echo Removing the previous build...
for /f "delims=" %%F in ('dir /b /a-d "%HERE%" 2^>nul') do if /i not "%%F"=="%MYSELF%" del /q "%HERE%\%%F"
for /f "delims=" %%D in ('dir /b /ad "%HERE%" 2^>nul') do rd /s /q "%HERE%\%%D"

echo Unpacking...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%ZIP%' -DestinationPath '%HERE%' -Force"
del /q "%ZIP%"
if not exist "%HERE%\VibeFactory.exe" (
    echo.
    echo Unpacking failed. Run this script again to fetch a fresh copy.
    set "FAILED=1"
    goto :done
)

rem Best effort: name the build that just arrived.
set "RELEASE="
for /f "delims=" %%N in ('powershell -NoProfile -Command "try { (Invoke-RestMethod https://api.github.com/repos/Fayberr/vibe-factory/releases/latest).name } catch { }" 2^>nul') do set "RELEASE=%%N"
if defined RELEASE echo.
if defined RELEASE echo Updated to: %RELEASE%
echo Done. Run VibeFactory.exe to play.
goto :done

:done
popd
rem A clean update closes the window by itself. Only a problem keeps it open long
rem enough to read what it says.
if defined FAILED (
    echo.
    pause
)
endlocal
