@echo off
rem Vibe Factory updater. Put this file in the folder you play the game from and
rem double-click it: it fetches the newest release, replaces everything in this
rem folder except this script, and starts nothing. Your saves are not here (they
rem live in Godot's user folder), so they are never touched.
rem Revision 3 (2026-09-29): redraw a slow download instead of waiting it out.
setlocal EnableExtensions
title Vibe Factory updater
pushd "%~dp0" || (echo Could not open the folder this script is in. & pause & exit /b 1)

set "URL=https://github.com/Fayberr/vibe-factory/releases/latest/download/VibeFactory-Windows.zip"
set "ZIP=%TEMP%\VibeFactory-Windows.zip"
set "MYSELF=%~nx0"
set "HERE=%CD%"

echo Vibe Factory updater
echo Folder: %HERE%
echo.

rem The game cannot be replaced while it holds its own files open.
tasklist /fi "imagename eq VibeFactory.exe" 2>nul | find /i "VibeFactory.exe" >nul
if not errorlevel 1 (
    echo VibeFactory.exe is running. Close the game and run this again.
    echo Nothing was changed.
    goto :done
)

rem First run in a fresh folder: make sure wiping it is really what is wanted.
if exist "%HERE%\VibeFactory.exe" goto :download
echo This folder does not have VibeFactory.exe in it yet.
echo Everything in it except this script will be deleted.
set /p OK="Type y and press Enter to continue: "
if /i not "%OK%"=="y" goto :cancelled

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
    goto :done
)
set "SIZE=0"
for %%A in ("%ZIP%") do set "SIZE=%%~zA"
rem 87 MB today. Anything under 20 MB is a truncated download or an error page.
if %SIZE% LSS 20000000 (
    echo.
    echo That download is too small to be the game. Nothing was changed.
    del /q "%ZIP%"
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
    goto :done
)

rem Best effort: name the build that just arrived.
set "RELEASE="
for /f "delims=" %%N in ('powershell -NoProfile -Command "try { (Invoke-RestMethod https://api.github.com/repos/Fayberr/vibe-factory/releases/latest).name } catch { }" 2^>nul') do set "RELEASE=%%N"
if defined RELEASE echo.
if defined RELEASE echo Updated to: %RELEASE%
echo Done. Run VibeFactory.exe to play.
goto :done

:cancelled
echo Cancelled. Nothing was changed.

:done
popd
echo.
pause
endlocal
