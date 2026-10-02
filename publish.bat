@echo off
setlocal
cd /d "%~dp0"
set OUT=%CD%\publish\app
rmdir /s /q "%CD%\publish" 2>nul
mkdir "%OUT%\Updater"

dotnet publish src\WOJD.LocalizationStudio\WOJD.LocalizationStudio.csproj -c Release -r win-x64 --self-contained true -o "%OUT%"
if errorlevel 1 exit /b %errorlevel%

dotnet publish src\WOJD.LocalizationStudio.Updater\WOJD.LocalizationStudio.Updater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "%CD%\publish\updater"
if errorlevel 1 exit /b %errorlevel%

copy /y "%CD%\publish\updater\WOJD-Localization-Studio.Updater.exe" "%OUT%\Updater\WOJD-Localization-Studio.Updater.exe" >nul
for /f "tokens=2 delims=<>" %%V in ('findstr /c:"<Version>" src\WOJD.LocalizationStudio\WOJD.LocalizationStudio.csproj') do echo|set /p=%%V>"%OUT%\version.txt"

echo.
echo Published to: %OUT%
