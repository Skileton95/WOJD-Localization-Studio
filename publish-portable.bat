@echo off
setlocal
cd /d "%~dp0"

echo Closing running WOJD Localization Studio...
taskkill /IM WOJD.LocalizationStudio.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

echo Publishing portable win-x64 build...
dotnet publish src\WOJD.LocalizationStudio\WOJD.LocalizationStudio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\win-x64
if errorlevel 1 pause & exit /b 1

echo.
echo Portable build complete: publish\win-x64\WOJD.LocalizationStudio.exe
pause
