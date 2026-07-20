#
# 실제 경로를 채워 넣은 실행 스크립트입니다.
# tools\Run-ProgressViewpointExport.ps1 (경로를 명령줄 인자로만 받는 범용
# 스크립트)을, 아래 고정된 값들로 호출만 해주는 얇은 래퍼입니다.
#
# 값이 바뀌면 (예: NWD가 다른 폴더로 옮겨지거나, Navisworks 설치 경로가
# 다르면) 아래 값만 고쳐서 저장하면 됩니다.
#
# 주의: 이 파일은 한글 경로가 직접 들어있어서 반드시 UTF-8 (BOM 포함)으로
# 저장되어 있어야 PowerShell이 깨지지 않고 읽습니다 - 메모장 등으로 열어서
# 저장할 때도 "UTF-8" (BOM 있는 쪽) 인코딩을 유지해주세요.
#

$here = Split-Path -Parent $MyInvocation.MyCommand.Path

& (Join-Path $here 'Run-ProgressViewpointExport.ps1') `
    -RoamerExe "C:\Program Files\Autodesk\Navisworks Simulate 2022\Roamer.exe" `
    -SourceFolder "M:\06.PM\19. Digitalization\NavisVisualizer\Export_NWD\생산공유" `
    -OutputDir "M:\06.PM\19. Digitalization\NavisVisualizer\Export_NWD\생산공유\Captured_Progress" `
    -ViewpointsXmlPath "M:\06.PM\19. Digitalization\NavisVisualizer\Export_NWD\생산공유\Captured_Progress\Trion_관측점.xml"

exit $LASTEXITCODE
