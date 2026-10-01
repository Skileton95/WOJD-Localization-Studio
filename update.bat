@echo off
setlocal
cd /d "%~dp0"

set "FROM_APP=0"
if /I "%~1"=="--from-app" set "FROM_APP=1"

echo ==============================================
echo   WOJD Localization Studio - Update
echo ==============================================
echo.

where git >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Git is not installed or not available in PATH.
    echo Install Git for Windows and run this file again.
    pause
    exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK is not installed or not available in PATH.
    pause
    exit /b 1
)

if not exist ".git" (
    echo [ERROR] This folder is not a Git clone.
    echo Clone the repository once, then use update.bat for future updates.
    pause
    exit /b 1
)

echo [1/4] Closing WOJD Localization Studio...
taskkill /IM WOJD.LocalizationStudio.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

echo [2/4] Checking local changes...
git status --porcelain > "%TEMP%\wojd_loc_status.txt"
for %%A in ("%TEMP%\wojd_loc_status.txt") do if %%~zA GTR 0 (
    echo.
    echo [ERROR] Local source changes detected.
    echo Commit, discard, or stash them before updating:
    type "%TEMP%\wojd_loc_status.txt"
    del "%TEMP%\wojd_loc_status.txt" >nul 2>&1
    pause
    exit /b 1
)
del "%TEMP%\wojd_loc_status.txt" >nul 2>&1

echo [3/4] Downloading latest source...
git pull --ff-only origin main
if errorlevel 1 (
    echo.
    echo [ERROR] Git update failed.
    pause
    exit /b 1
)

echo [4/4] Building Release...
dotnet restore WOJD.LocalizationStudio.sln
if errorlevel 1 pause & exit /b 1

dotnet build WOJD.LocalizationStudio.sln -c Release --no-restore
if errorlevel 1 pause & exit /b 1

set "APP_EXE=%CD%\src\WOJD.LocalizationStudio\bin\Release\net8.0-windows\WOJD.LocalizationStudio.exe"

echo.
echo ==============================================
echo Update complete.
echo EXE: %APP_EXE%
echo ==============================================

if "%FROM_APP%"=="1" (
    timeout /t 1 /nobreak >nul
    start "" "%APP_EXE%"
    exit /b 0
)

pause
