@echo off
setlocal
cd /d "%~dp0"
dotnet build WOJD.LocalizationStudio.sln -c Debug
if errorlevel 1 exit /b %errorlevel%
echo.
echo Build completed.
