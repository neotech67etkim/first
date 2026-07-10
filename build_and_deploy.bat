@echo off
setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "SLN=%SCRIPT_DIR%NavisTreeExporter.sln"
set "OUT_DIR=%SCRIPT_DIR%src\NavisTreeExporter\bin\x64\Release\net48"
set "PLUGIN_DIR=C:\ProgramData\Autodesk\Navisworks Simulate 2022\Plugins\NavisTreeExporter"

echo ============================================
echo  NavisTreeExporter - Build and Deploy
echo ============================================
echo.

rem --- 1. Locate the dotnet CLI (included with Visual Studio 2022) ---
where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet CLI not found.
    echo Make sure Visual Studio 2022 is installed with the ".NET desktop development" workload.
    echo.
    pause
    exit /b 1
)

echo [1/3] Building (Release / x64)...
echo.
dotnet build "%SLN%" -c Release -p:Platform=x64
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. See the errors above.
    echo.
    pause
    exit /b 1
)

if not exist "%OUT_DIR%\NavisTreeExporter.dll" (
    echo.
    echo [ERROR] Build succeeded but the output file was not found: %OUT_DIR%
    echo.
    pause
    exit /b 1
)

:check_running
tasklist /FI "IMAGENAME eq Roamer.exe" 2>nul | find /I "Roamer.exe" >nul
if not errorlevel 1 (
    echo.
    echo [WARNING] Navisworks Simulate 2022 is currently running.
    echo The previously installed plugin DLL is locked while Navisworks is open,
    echo so it cannot be overwritten. Please close Navisworks now.
    echo.
    pause
    goto :check_running
)

echo.
echo [2/3] Copying to the Navisworks Plugins folder...
echo   Target: %PLUGIN_DIR%
if not exist "%PLUGIN_DIR%" mkdir "%PLUGIN_DIR%" 2>nul
copy /Y "%OUT_DIR%\NavisTreeExporter.dll" "%PLUGIN_DIR%\" >nul
if errorlevel 1 goto :copy_failed
copy /Y "%OUT_DIR%\Newtonsoft.Json.dll" "%PLUGIN_DIR%\" >nul
if errorlevel 1 goto :copy_failed

echo.
echo [3/3] Done!
echo.
echo Restart Navisworks Simulate 2022 and look for the
echo "Export Selection Tree" button under the Add-ins ribbon tab.
echo.
pause
exit /b 0

:copy_failed
echo.
echo [ERROR] Failed to copy files to: %PLUGIN_DIR%
echo Make sure Navisworks Simulate 2022 is fully closed, then run this script again.
echo If it still fails, right-click this .bat file and choose "Run as administrator".
echo.
pause
exit /b 1
