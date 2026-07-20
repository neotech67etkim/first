@echo off
setlocal

set "TASK_NAME=NavisTreeExporter Progress Export"

schtasks /Delete /TN "%TASK_NAME%" /F

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Could not delete the task - it may not exist. See the error above.
    pause
    exit /b 1
)

echo.
echo Removed scheduled task: %TASK_NAME%
pause
