@echo off
rem Vibe Factory updater. Put this file in the folder you play the game from and
rem double-click it: it fetches the newest release, replaces everything in this
rem folder except this script, and starts nothing. Your saves are not here (they
rem live in Godot's user folder), so they are never touched.
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
if not errorlevel 1 (
    curl.exe -L -f --retry 3 -o "%ZIP%" "%URL%"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -Uri '%URL%' -OutFile '%ZIP%'"
)
if not exist "%ZIP%" (
    echo.
    echo Download failed. Nothing was changed, the build in this folder still works.
    goto :done
)
set "SIZE=0"
for %%A in ("%ZIP%") do set "SIZE=%%~zA"
if %SIZE% LSS 1000000 (
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
