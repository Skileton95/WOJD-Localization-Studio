@echo off
setlocal
cd /d "%~dp0"

echo Closing running WOJD Localization Studio...
taskkill /IM WOJD.LocalizationStudio.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

echo Restoring packages...
dotnet restore WOJD.LocalizationStudio.sln
if errorlevel 1 pause & exit /b 1

echo Building Release...
dotnet build WOJD.LocalizationStudio.sln -c Release
if errorlevel 1 pause & exit /b 1

echo.
echo Build complete.
echo EXE: src\WOJD.LocalizationStudio\bin\Release\net8.0-windows\WOJD.LocalizationStudio.exe
pause
