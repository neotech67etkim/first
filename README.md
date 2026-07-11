# NavisTreeExporter

Navisworks Simulate용 애드인 모음. 두 개의 리본 버튼(Add-ins 탭)을 제공합니다.

- **Export Selection Tree** — 열려 있는 모델의 **선택 트리(Selection Tree)**를
  읽어 **JSON**과 **CSV**로 저장
- **Export Viewpoint Images** — 저장된 관측점(Saved Viewpoints)마다 카메라를
  이동시켜 화면을 캡처, PNG로 저장

## Export Selection Tree

실행 시 내보낼 정보 범위를 3단계 중에서 고를 수 있습니다.

1. **이름 + 계층 구조만** — `DisplayName`과 부모/자식 관계만. 가장 가볍고 빠름.
2. **이름 + 계층 + 기본 정보** — `ClassName`/`ClassDisplayName`/`InstanceGuid`/
   `HasGeometry`/`IsHidden` 추가.
3. **이름 + 계층 + 전체 속성** — `PropertyCategories`/`Properties`까지 포함.
   항목당 API 호출이 훨씬 많아 가장 느리고, 대용량 모델에서는 결과 파일도
   수 GB까지 커질 수 있습니다.

세 옵션 모두 **"형상이 있는 항목만" 체크박스**를 함께 켤 수 있습니다.
`HasGeometry == false`인 폴더/그룹/레이어 같은 순수 조직용 컨테이너 노드를
건너뛰고, 그 자식들은 원래 있던 자리(스킵된 노드의 부모 밑)로 그대로
이어붙입니다 — 계층 구조는 그대로 유지하면서 실제 형상이 없는 중간 노드만
빠지므로, 트리 크기(및 API 호출 수)가 크게 줄어들 수 있습니다.

추출한 데이터는 이후 다른 설계 자료(BIM 모델, 물량 리스트 등)와 비교하여
트리 내 항목들을 식별·매칭하는 후속 작업의 입력으로 사용됩니다.

## 구성

```
src/NavisTreeExporter/
  Core/
    ExportDetailLevel.cs         3단계 내보내기 범위 (이름만 / +기본정보 / +속성)
    ExportProgressReporter.cs    진행률 콜백 + 취소 체크
    FileNameSanitizer.cs         파일명으로 못 쓰는 문자 치환 (공용)
    SavedViewpointCollector.cs   Document.SavedViewpoints 재귀 순회
    AutoExportSettings.cs        Export Viewpoint Images 자동 모드 환경변수 설정
  Export/
    TreeExportWriter.cs          트리를 한 번만 순회하며 JSON/CSV를 동시에
                                  파일로 스트리밍 (트리 전체를 메모리에
                                  올리지 않음 - 대용량 모델의 메모리 고갈 방지)
  Plugin/
    ExportTreeAddin.cs           AddInPlugin (Export Selection Tree 버튼)
    ExportOptionsForm.cs         내보내기 범위 선택 다이얼로그
    ExportProgressForm.cs        진행률 다이얼로그 (취소 버튼 포함, 공용)
    ExportViewpointImagesAddin.cs  AddInPlugin (Export Viewpoint Images 버튼)
    ViewpointCaptureService.cs   화면 캡처/크롭 + 자동 모드 내보내기 로직
                                  (버튼 방식/자동 모드 공용)
    ExportViewpointImagesAutoWatcher.cs  EventWatcherPlugin (자동 모드 부트스트랩,
                                  버튼 없이 시작 시 자동 로드됨)
```

트리 전체를 먼저 메모리에 읽어들인 뒤 내보내는 구조(DTO 트리 → JSON/CSV
변환)를 시도했으나, 속성까지 포함해서 읽을 때 메모리 사용량이 너무 커져
시스템이 먹통이 되는 문제가 있어 항목을 하나씩 순회하며 그 자리에서 바로
파일에 쓰는 스트리밍 구조로 바꿨습니다.

## 출력 형식

- **JSON**: 트리 구조를 그대로 중첩된 형태로 저장 (`Children`으로 재귀). 선택한
  범위에 따라 `ClassName`/`ClassDisplayName`/`InstanceGuid`/`HasGeometry`/
  `IsHidden`/`PropertyCategories` 필드가 포함되거나 생략됩니다.
- **CSV (items)**: 항목 1개 = 1행.
  - "이름+계층만": `Path, DisplayName, ParentPath, Depth`
  - "기본 정보" 이상: `Path, DisplayName, ClassName, ClassDisplayName,
    InstanceGuid, ParentPath, HasGeometry, IsHidden, Depth`
- **CSV (properties)**: 항목의 속성 1개 = 1행 (long format, 이후 pivot/조인이
  쉽도록). `ItemPath, InstanceGuid, CategoryName, CategoryDisplayName,
  PropertyName, PropertyDisplayName, Value, DataType` — "전체 속성" 모드에서만
  생성됩니다.

`ClassDisplayName`/`CategoryDisplayName`/속성 `DisplayName`은 Navisworks
API가 한글 Windows에서 로컬라이즈 라벨을 깨진 인코딩(UTF-8 → CP949 오판독)
으로 반환하는 문제가 있어, 내보내기 전에 자동으로 복구합니다
(`TreeExportWriter.FixMojibake`).

## Export Viewpoint Images

`Document.SavedViewpoints`를 재귀적으로 순회해 모든 저장된 관측점을 찾고,
각 관측점마다:

1. `Document.CurrentViewpoint`를 그 관측점의 카메라 상태로 이동
2. 화면 구석에 뜨는 작은 창에서 로딩 완료를 직접 확인하고 **캡처** 클릭
   (건너뛰기/전체 취소도 가능 — 자동 대기시간 방식은 로딩 시간이 항목마다
   달라서 신뢰할 수 없어 이 방식으로 바꿨습니다)
3. Navisworks 메인 윈도우를 화면 캡처(Win32 `GetWindowRect` +
   `Graphics.CopyFromScreen`)한 뒤, **3D 뷰포트 영역으로 크롭**해서 PNG로 저장

실제 렌더링을 오프스크린이 아니라 **화면에 보이는 그대로 캡처**하는 방식이라,
실행 중에는 Navisworks 창이 최소화되지 않고 화면에 보여야 합니다.

크롭 범위는 "화면에 보이는 가장 큰 말단(leaf) 창"을 3D 뷰포트로 추정한 뒤,
**선택 트리 / 저장된 관측점 패널의 실제 창 좌표를 읽어서** 그 패널이 겹치는
쪽 경계를 안쪽으로 당기는 방식으로 계산합니다 (두 패널은 뷰포트 위에
오버레이로 그려지는 구조라, 뷰포트 좌표만으로는 안 걸러짐). 창을
숨겼다가 다시 보여주는 방식도 시도했으나, 패널이 아닌 핵심 프레임 창까지
숨겨져 레이아웃이 깨지는 문제가 있어 **창 상태는 전혀 건드리지 않는**
현재 방식(좌표만 읽어서 캡처 후 크롭)으로 정착했습니다. 실제 모델로 정상
동작 확인 완료.

### 자동 실행(무인 배치) 모드

매일 새로 갱신되는 NWD 파일을 사람 없이 자동으로 처리하기 위한 모드입니다.
**이미지 생성까지만** 담당하고, 생성된 이미지를 서버에 올리는 작업은 별도
스크립트/프로세스가 맡는 구조로 분리되어 있습니다.

Navisworks를 실행하는 프로세스(예: 작업 스케줄러가 띄우는 스크립트)가 아래
환경 변수를 설정한 뒤 파일을 열면, 플러그인이 버튼 클릭 없이 자동으로
동작합니다.

| 환경 변수 | 필수 | 설명 |
|---|---|---|
| `NAVIS_AUTO_EXPORT_IMAGES` | 예 | `1`이어야 자동 모드가 켜짐 |
| `NAVIS_AUTO_OUTPUT_DIR` | 예 | 이미지를 저장할 상위 폴더 |
| `NAVIS_AUTO_WAIT_SECONDS` | 아니오 | 관측점 이동 후 캡처까지 대기 시간(초). 기본 8초 |

동작 방식:

1. `ExportViewpointImagesAddin`(리본 버튼, `AddInPlugin`)은 버튼을 눌러야만
   실행되는 구조라 — 실제 빌드해보니 여기에 `Load()`/`Unload()`를
   추가하는 첫 시도는 `CS0115`(재정의할 메서드 없음) 오류로 확인됐습니다 —
   자동 모드는 **`ExportViewpointImagesAutoWatcher`라는 별도의
   `EventWatcherPlugin`**이 담당합니다. 이 플러그인 종류는 Navisworks가
   시작할 때 버튼 클릭과 무관하게 자동으로 로드되고, `OnLoaded()`/
   `OnUnloading()`을 오버라이드해 API 이벤트 구독 등을 붙이는 것이 SDK의
   표준 패턴입니다.
2. `OnLoaded()`에서 자동 모드 환경 변수가 있으면 1초 간격으로 문서가
   열렸는지 폴링 시작 (최대 10분 대기, 그 안에 안 열리면 포기하고 종료).
3. 문서가 열리면 저장된 관측점을 모두 순회하며, 사람이 누르는 **캡처**
   버튼 대신 **관측점 이동 후 고정 대기시간**만큼 기다렸다가 캡처합니다.
   (대화상자를 띄우면 아무도 클릭할 사람이 없어 영원히 멈추므로, 자동
   모드에서는 MessageBox를 전혀 띄우지 않습니다.) 캡처/크롭 로직 자체는
   버튼 방식과 완전히 동일한 코드(`ViewpointCaptureService`)를 공유합니다.
4. `NAVIS_AUTO_OUTPUT_DIR\<파일명>_<타임스탬프>\` 폴더를 만들어 그 안에
   PNG들과 `_export_log.txt`(진행 로그)를 저장하고, 모두 끝나면
   `_COMPLETE.txt` 마커 파일을 씁니다 — 업로드 스크립트는 이 마커가 있는
   폴더만 골라서 올리면 아직 다 안 끝난 폴더를 건드리는 일을 피할 수
   있습니다.
5. 끝나면(성공/실패 상관없이) 프로세스가 스스로 종료됩니다 — 작업
   스케줄러 작업이 정상적으로 마무리됩니다.

Windows 작업 스케줄러에 등록해서 매일 실행하려면
[`tools/Run-DailyViewpointExport.ps1`](./tools/Run-DailyViewpointExport.ps1)를
사용하세요. 지정한 폴더에서 `*.nwf`/`*.nwd` 중 가장 최근에 수정된 파일을 찾아
Navisworks를 실행하고, 위 환경 변수를 설정해준 뒤 종료를 기다립니다
(파일 상단 주석에 `schtasks` 등록 예시 포함).

`EventWatcherPlugin`이 버튼 없이도 시작 시 자동으로 로드된다는 점과
`document.Models.Count`로 "문서가 다 열렸는지"를 판단하는 방식,
`document.CurrentFileName` 프로퍼티명은 아직 실제로 컴파일·실행해보지
못한 부분입니다 — 지금까지처럼 실제 빌드 오류를 보면서 맞춰나가야 할 수
있습니다.

## 빌드 & 배포

가장 빠른 방법: 저장소 루트의 **`build_and_deploy.bat`을 더블클릭**하면
Release/x64로 빌드한 뒤 Navisworks Plugins 폴더까지 자동으로 복사합니다.
(dotnet CLI가 PATH에 있어야 함 — Visual Studio 2022 설치 시 기본 포함)

- [docs/BUILD.md](./docs/BUILD.md) — Windows에서 수동으로 빌드하는 방법
- [docs/DEPLOY.md](./docs/DEPLOY.md) — Navisworks Plugins 폴더에 설치하는 방법

## 요구 사항

- Navisworks Simulate 2022 (.NET Framework 4.8)
- Windows + Visual Studio 2022 (빌드 시에만 필요)

## 알려진 제약사항

이 코드는 Navisworks SDK/API DLL이 없는 환경(Linux 컨테이너)에서 작성되어
초기에는 실제 컴파일로 검증하지 못한 상태로 개발했습니다. 초기 버전은
커스텀 리본 탭(`[RibbonTab]`/`[RibbonGroup]`)으로 구현했으나
`RibbonGroupAttribute`가 실제 API에 없어 컴파일 오류가 발생했고, 이를
확인 후 훨씬 표준적인 `AddInPlugin` 패턴(Add-ins 탭에 버튼 자동 생성)으로
교체했습니다. `Core`/`Export` 네임스페이스의 `ModelItem`/`PropertyCategory`/
`DataProperty` 관련 코드(Export Selection Tree)와, `SavedViewpointCollector`/
`ExportViewpointImagesAddin`의 저장된 관측점 API(`FolderItem`,
`SavedViewpoint`, `Document.SavedViewpoints.RootItem`,
`Document.CurrentViewpoint.CopyFrom`) 모두 실제 Windows 환경에서 빌드 및
실행까지 검증 완료했습니다.

## 다음 단계 (제안)

- [x] 내보내기 범위 3단계 선택 (이름만 / +기본정보 / +전체속성)
- [x] 대용량 모델 대응을 위한 스트리밍 내보내기 (메모리에 트리 전체를 올리지 않음)
- [x] 로컬라이즈 라벨 인코딩(mojibake) 자동 복구
- [x] Export Viewpoint Images 플러그인 (저장된 관측점 스크린샷 일괄 저장,
      실제 모델로 빌드·실행 검증 완료)
- [ ] Export Viewpoint Images 자동(무인) 모드 — 매일 최신 NWD를 찾아 실행,
      고정 대기시간으로 캡처, 서버 업로드는 별도 스크립트 (컴파일/실행 미검증)
- [ ] 초대형 모델(수천만 항목, 속성 포함)의 결과 파일 자체가 수 GB로 커지는
      문제 — 압축 저장(.gz), 필요한 속성만 필터링해서 내보내기, JSON 생략(CSV만)
      등의 옵션 중 방향 결정 필요
- [ ] 현재 선택된 항목만 내보내는 옵션 (전체 트리 대신)
- [ ] 매우 깊은 트리 대응을 위한 재귀 → 반복(iterative) 순회 전환 (현재는 재귀
      호출 스택 깊이가 트리 깊이에 비례 - 일반적인 모델에서는 문제없음)
- [ ] 단위 테스트용 Mock/추상화 레이어 (Navisworks API는 직접 단위 테스트 불가)
