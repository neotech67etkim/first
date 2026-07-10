@echo off
setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "SLN=%SCRIPT_DIR%NavisTreeExporter.sln"
set "OUT_DIR=%SCRIPT_DIR%src\NavisTreeExporter\bin\x64\Release\net48"
set "PLUGIN_DIR=C:\ProgramData\Autodesk\Navisworks Simulate 2022\Plugins\NavisTreeExporter"

echo ============================================
echo  NavisTreeExporter - Build ^& Deploy
echo ============================================
echo.

rem --- 1. dotnet CLI 찾기 (Visual Studio 2022 설치 시 기본 포함) ---
where dotnet >nul 2>nul
if errorlevel 1 (
    echo [오류] dotnet CLI를 찾을 수 없습니다.
    echo Visual Studio 2022를 ".NET desktop development" 워크로드와 함께 설치했는지 확인하세요.
    echo.
    pause
    exit /b 1
)

echo [1/3] 빌드 중 (Release / x64)...
echo.
dotnet build "%SLN%" -c Release -p:Platform=x64
if errorlevel 1 (
    echo.
    echo [오류] 빌드에 실패했습니다. 위 오류 메시지를 확인하세요.
    echo.
    pause
    exit /b 1
)

if not exist "%OUT_DIR%\NavisTreeExporter.dll" (
    echo.
    echo [오류] 빌드는 성공했지만 결과물을 찾을 수 없습니다: %OUT_DIR%
    echo.
    pause
    exit /b 1
)

echo.
echo [2/3] Navisworks Plugins 폴더로 복사 중...
echo   대상: %PLUGIN_DIR%
if not exist "%PLUGIN_DIR%" mkdir "%PLUGIN_DIR%" 2>nul
copy /Y "%OUT_DIR%\NavisTreeExporter.dll" "%PLUGIN_DIR%\" >nul
if errorlevel 1 goto :copy_failed
copy /Y "%OUT_DIR%\Newtonsoft.Json.dll" "%PLUGIN_DIR%\" >nul
if errorlevel 1 goto :copy_failed

echo.
echo [3/3] 완료!
echo.
echo Navisworks Simulate 2022를 (다시) 실행하면 리본의 Add-ins 탭에서
echo "Export Selection Tree" 버튼을 사용할 수 있습니다.
echo.
pause
exit /b 0

:copy_failed
echo.
echo [오류] 파일 복사에 실패했습니다: %PLUGIN_DIR%
echo 이 창을 닫고, 이 .bat 파일을 마우스 우클릭 ^> "관리자 권한으로 실행"으로 다시 시도해보세요.
echo (Navisworks가 실행 중이면 DLL이 잠겨 있을 수 있으니 먼저 종료해주세요.)
echo.
pause
exit /b 1
