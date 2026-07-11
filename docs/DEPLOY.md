# 배포(설치) 방법

Navisworks는 시작 시 `Plugins` 폴더 아래의 서브폴더들을 스캔해서
`[Plugin]` 특성이 붙은 클래스를 자동으로 로드합니다. 별도의 레지스트리
등록이나 특별한 파일명 규칙은 필요 없습니다.

## 자동 배포 (권장)

저장소 루트의 `build_and_deploy.bat`을 더블클릭하면 빌드부터 복사까지
자동으로 처리됩니다. 복사가 "관리자 권한으로 실행"을 요구하면 안내
메시지대로 다시 실행하세요.

## 수동 배포

1. [BUILD.md](./BUILD.md)대로 Release 빌드를 생성합니다.
2. `bin\x64\Release\net48\` 폴더의 다음 파일들을 복사합니다.
   - `NavisTreeExporter.dll`
   - `Newtonsoft.Json.dll`
3. Navisworks Plugins 폴더 아래에 전용 서브폴더를 만들어 붙여넣습니다.

   ```
   C:\ProgramData\Autodesk\Navisworks Simulate 2022\Plugins\NavisTreeExporter\
   ```

4. Navisworks를 (다시) 실행하면 리본의 **Add-ins** 탭 아래 **Export Selection
   Tree** 버튼이 나타납니다.

## 사용법

1. NWD/NWC/모델 파일을 Navisworks에서 엽니다.
2. **Add-ins** 탭의 **Export Selection Tree** 버튼을 클릭합니다.
3. 내보낼 정보 범위를 선택합니다.
   - **이름 + 계층 구조만** — 가장 가볍고 빠름
   - **이름 + 계층 + 기본 정보** — 클래스, GUID 등 추가
   - **이름 + 계층 + 전체 속성** — 가장 느림, 대용량 모델에서는 결과 파일이
     수 GB까지 커질 수 있음
4. 저장할 폴더를 선택합니다.
5. 선택한 범위에 따라 다음 파일이 생성됩니다.
   - `<문서명>_<타임스탬프>.json` — 선택 트리(계층 구조, 범위에 따라 기본
     정보/속성 포함)
   - `<문서명>_<타임스탬프>_items.csv` — 트리 계층 구조
   - `<문서명>_<타임스탬프>_properties.csv` — 항목별 속성 목록 (long format,
     "전체 속성" 선택 시에만 생성)
