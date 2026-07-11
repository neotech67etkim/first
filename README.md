# NavisTreeExporter

Navisworks Simulate용 애드인. 열려 있는 모델의 **선택 트리(Selection Tree)**를
읽어 **JSON**과 **CSV**로 저장합니다. 실행 시 내보낼 정보 범위를 3단계 중에서
고를 수 있습니다.

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
    ExportDetailLevel.cs        3단계 내보내기 범위 (이름만 / +기본정보 / +속성)
    ExportProgressReporter.cs   진행률 콜백 + 취소 체크
  Export/
    TreeExportWriter.cs         트리를 한 번만 순회하며 JSON/CSV를 동시에
                                 파일로 스트리밍 (트리 전체를 메모리에
                                 올리지 않음 - 대용량 모델의 메모리 고갈 방지)
  Plugin/
    ExportTreeAddin.cs      AddInPlugin (Add-ins 탭 > Export Selection Tree 버튼)
    ExportOptionsForm.cs    내보내기 범위 선택 다이얼로그
    ExportProgressForm.cs   진행률 다이얼로그 (취소 버튼 포함)
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
**실제 컴파일로 완전히 검증하지 못했습니다.** 초기 버전은 커스텀 리본
탭(`[RibbonTab]`/`[RibbonGroup]`)으로 구현했으나 `RibbonGroupAttribute`가
실제 API에 없어 컴파일 오류가 발생했고, 이를 확인 후 훨씬 표준적인
`AddInPlugin` 패턴(Add-ins 탭에 버튼 자동 생성)으로 교체했습니다.
`AddInPlugin`/`[Plugin]`/`[AddInPlugin(AddInLocation.AddIn)]`은 Navisworks
SDK 샘플 전반에서 쓰이는 안정적인 조합이라 신뢰도가 더 높지만, 여전히
Windows에서 처음 빌드할 때 오류가 날 수 있습니다. 나머지 코드(`Core`,
`Export` 네임스페이스)는 표준 `Autodesk.Navisworks.Api`의
`ModelItem`/`PropertyCategory`/`DataProperty` 멤버만 사용하므로 상대적으로
안정적입니다.

## 다음 단계 (제안)

- [x] 내보내기 범위 3단계 선택 (이름만 / +기본정보 / +전체속성)
- [x] 대용량 모델 대응을 위한 스트리밍 내보내기 (메모리에 트리 전체를 올리지 않음)
- [x] 로컬라이즈 라벨 인코딩(mojibake) 자동 복구
- [ ] 초대형 모델(수천만 항목, 속성 포함)의 결과 파일 자체가 수 GB로 커지는
      문제 — 압축 저장(.gz), 필요한 속성만 필터링해서 내보내기, JSON 생략(CSV만)
      등의 옵션 중 방향 결정 필요
- [ ] 현재 선택된 항목만 내보내는 옵션 (전체 트리 대신)
- [ ] 매우 깊은 트리 대응을 위한 재귀 → 반복(iterative) 순회 전환 (현재는 재귀
      호출 스택 깊이가 트리 깊이에 비례 - 일반적인 모델에서는 문제없음)
- [ ] 단위 테스트용 Mock/추상화 레이어 (Navisworks API는 직접 단위 테스트 불가)
