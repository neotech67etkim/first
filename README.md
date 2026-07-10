# NavisTreeExporter

Navisworks Manage용 애드인. 열려 있는 모델의 **선택 트리(Selection Tree)**를
계층 구조와 속성(PropertyCategories/Properties)까지 포함해 읽어들여
**JSON**과 **CSV**로 저장합니다.

추출한 데이터는 이후 다른 설계 자료(BIM 모델, 물량 리스트 등)와 비교하여
트리 내 항목들을 식별·매칭하는 후속 작업의 입력으로 사용됩니다.

## 구성

```
src/NavisTreeExporter/
  Core/
    TreeNode.cs           트리 노드 / 속성 DTO
    ModelTreeReader.cs    Document -> TreeNode 트리로 변환
  Export/
    JsonTreeExporter.cs   전체 트리(JSON, 중첩 구조)
    CsvTreeExporter.cs    items.csv(계층) + properties.csv(속성, long format)
  Plugin/
    ExportTreeAddin.cs    리본 버튼 커맨드 (Tree Export > Export Tree)
```

## 출력 형식

- **JSON**: 트리 구조를 그대로 중첩된 형태로 저장 (`Children`으로 재귀).
- **CSV (items)**: 항목 1개 = 1행. `Path, DisplayName, ClassName,
  ClassDisplayName, InstanceGuid, ParentPath, HasGeometry, IsHidden, Depth`
- **CSV (properties)**: 항목의 속성 1개 = 1행 (long format, 이후 pivot/조인이
  쉽도록). `ItemPath, InstanceGuid, CategoryName, CategoryDisplayName,
  PropertyName, PropertyDisplayName, Value, DataType`

## 빌드 & 배포

- [docs/BUILD.md](./docs/BUILD.md) — Windows에서 빌드하는 방법
- [docs/DEPLOY.md](./docs/DEPLOY.md) — Navisworks Plugins 폴더에 설치하는 방법

## 요구 사항

- Navisworks Manage 2022 (.NET Framework 4.8)
- Windows + Visual Studio 2022 (빌드 시에만 필요)

## 알려진 제약사항

이 코드는 Navisworks SDK/API DLL이 없는 환경(Linux 컨테이너)에서 작성되어
**실제 컴파일로 검증하지 못했습니다.** 특히 `Plugin/ExportTreeAddin.cs`의
리본 버튼 관련 특성(`[RibbonTab]`, `[RibbonGroup]`, `[Command]`)은 XAML 없이
특성만으로 리본을 구성하는 방식인데, 정확한 속성명·연결 방식이 Navisworks
SDK 버전에 따라 다를 수 있습니다. Visual Studio에서 Navisworks API 참조를
추가한 뒤 IntelliSense/빌드 오류를 보면서, 필요하면 Navisworks SDK에 포함된
Ribbon 샘플(보통 `...Navisworks Manage 2022 API\SDK\...\Plugins\Ribbon\`
경로)과 대조해 속성을 맞춰야 할 수 있습니다. 나머지 코드(`Core`, `Export`
네임스페이스)는 표준 `Autodesk.Navisworks.Api`의 `ModelItem`/`PropertyCategory`
/`DataProperty` 멤버만 사용하므로 상대적으로 안정적입니다.

## 다음 단계 (제안)

- [ ] 내보내기 옵션 다이얼로그 (JSON/CSV 선택, 속성 포함 여부, 파일명 지정)
- [ ] 현재 선택된 항목만 내보내는 옵션 (전체 트리 대신)
- [ ] 대용량 모델 대응을 위한 재귀 → 반복(iterative) 순회 전환
- [ ] 단위 테스트용 Mock/추상화 레이어 (Navisworks API는 직접 단위 테스트 불가)
