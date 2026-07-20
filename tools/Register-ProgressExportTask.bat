@echo off
setlocal

set "TASK_NAME=NavisTreeExporter Progress Export"
set "SCRIPT_DIR=%~dp0"
set "BAT_PATH=%SCRIPT_DIR%Run-ProgressViewpointExport.bat"

if not exist "%BAT_PATH%" (
    echo Could not find "%BAT_PATH%".
    echo Run this from inside the tools folder after a git pull.
    pause
    exit /b 1
)

rem Trigger: at logon, with a 5 minute delay - gives Windows time to
rem finish starting up and to mount the M: network drive before the
rem script tries to use it. Runs once per logon (normally once a day,
rem since this machine is logged in once in the morning).
rem
rem Deliberately NOT specifying /RU or /RP: leaving those out makes
rem schtasks register the task to run as the current user, in the
rem current interactive desktop session ("run only when user is
rem logged on"). This matters because the export does real screen
rem capture (not an off-screen render) - if the task were switched to
rem "run whether user is logged on or not" in Task Scheduler's GUI, it
rem would run in a hidden, non-interactive session and the screenshots
rem would come out blank/wrong. Leave that setting alone if you ever
rem open this task in taskschd.msc.
schtasks /Create /TN "%TASK_NAME%" /SC ONLOGON /DELAY 0005:00 /TR "\"%BAT_PATH%\"" /RL LIMITED /F

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Task registration failed - see the error above.
    pause
    exit /b 1
)

echo.
echo Registered scheduled task: %TASK_NAME%
echo   Trigger: at logon (+5 min delay)
echo   Action : %BAT_PATH%
echo.
echo View/edit it later in Task Scheduler (taskschd.msc), or remove it
echo with Unregister-ProgressExportTask.bat in this same folder.
echo.
echo IMPORTANT: the computer's screen needs to stay unlocked/active for
echo this to work correctly, since it's a real screenshot of the
echo Navisworks window, not an off-screen render.
pause
