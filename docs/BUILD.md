# 빌드 방법 (상세)

이 애드인은 **Windows + Navisworks Simulate 2022 (.NET Framework 4.8)** 환경에서만
빌드할 수 있습니다. Navisworks API DLL은 Autodesk SDK 라이선스 정책상
저장소에 포함하지 않으므로, 로컬에 설치된 Navisworks의 DLL을 참조합니다.
(Linux/Mac에서는 빌드가 불가능합니다.)

## 0. 준비물 체크리스트

| 항목 | 확인 방법 |
|---|---|
| Windows 10/11 | - |
| Navisworks Simulate 2022 설치됨 | `C:\Program Files\Autodesk\Navisworks Simulate 2022\Roamer.exe` 존재 여부 확인 |
| Visual Studio 2022 (Community 이상) | ".NET desktop development" 워크로드 설치 필수 |
| .NET Framework 4.8 Developer Pack | VS Installer의 "개별 구성 요소" 탭에서 "NET Framework 4.8 SDK / Targeting Pack" 체크 |
| 인터넷 연결 | NuGet에서 `Newtonsoft.Json` 패키지를 받아야 함 |

VS Installer에서 워크로드가 안 보이면: Visual Studio Installer 실행 →
설치된 항목 옆 "수정(Modify)" → 워크로드 탭에서 **.NET Desktop Development**
체크 → 개별 구성 요소 탭에서 **.NET Framework 4.8 targeting pack** 체크 →
수정.

## 1. 소스 코드 받기

```powershell
git clone https://github.com/neotech67etkim/first.git
cd first
git checkout claude/github-connection-issue-d3hzh9
```

이미 클론되어 있다면:

```powershell
git fetch origin claude/github-connection-issue-d3hzh9
git checkout claude/github-connection-issue-d3hzh9
git pull
```

## 2. Navisworks API DLL 경로 확인

프로젝트는 기본적으로 다음 경로를 참조합니다.

```
C:\Program Files\Autodesk\Navisworks Simulate 2022\
```

아래 3개 파일이 그 경로에 있는지 먼저 확인하세요.

```powershell
dir "C:\Program Files\Autodesk\Navisworks Simulate 2022\Autodesk.Navisworks.Api.dll"
dir "C:\Program Files\Autodesk\Navisworks Simulate 2022\Autodesk.Navisworks.Interop.ComApi.dll"
dir "C:\Program Files\Autodesk\Navisworks Simulate 2022\Autodesk.Navisworks.Interop.ComBridge.dll"
```

설치 경로가 다르다면(예: D 드라이브), 아래 3-B 방법으로 경로를 지정해야 합니다.

## 3-A. Visual Studio에서 빌드 (기본 경로인 경우)

1. `NavisTreeExporter.sln`을 더블클릭해서 Visual Studio로 엽니다.
2. 오른쪽 위 "솔루션 탐색기"에서 처음 열면 NuGet 패키지를 자동 복원합니다.
   (안 되면 솔루션 우클릭 → **NuGet 패키지 복원**)
3. 상단 툴바에서 구성(Configuration)을 **Release**, 플랫폼을 **x64**로 맞춥니다.
   (Navisworks는 64비트 전용이라 프로젝트에 x64로 고정되어 있습니다.)
4. 메뉴 **빌드(Build) → 솔루션 빌드(Build Solution)** (단축키 `Ctrl+Shift+B`).
5. 하단 "출력" 창에 `빌드: 성공 1개`가 뜨면 완료입니다.

## 3-B. Navisworks 설치 경로가 기본값과 다른 경우

두 가지 방법 중 편한 쪽을 선택하세요.

**방법 1) 프로젝트 파일의 기본 경로를 직접 수정**

`src\NavisTreeExporter\NavisTreeExporter.csproj` 파일을 열어 아래 줄을
본인 설치 경로로 바꿉니다.

```xml
<NavisworksInstallDir Condition="'$(NavisworksInstallDir)' == ''">D:\Autodesk\Navisworks Simulate 2022\</NavisworksInstallDir>
```

끝에 `\`를 꼭 붙여야 합니다. 저장 후 Visual Studio에서 다시 빌드하면 됩니다.

**방법 2) 명령줄 빌드 시 옵션으로 전달 (파일 수정 없이)**

CLI로 빌드할 때만 아래처럼 경로를 넘기면 `.csproj`을 건드리지 않아도 됩니다.
(4번 항목 참고)

## 4. 명령줄(CLI)로 빌드

Visual Studio "Developer PowerShell" 또는 `dotnet` CLI가 설치된 일반
PowerShell/CMD에서:

```powershell
cd first
dotnet build NavisTreeExporter.sln -c Release
```

설치 경로가 기본값과 다르면:

```powershell
dotnet build NavisTreeExporter.sln -c Release `
  /p:NavisworksInstallDir="D:\Autodesk\Navisworks Simulate 2022\"
```

(CMD에서는 줄바꿈에 `^`, PowerShell에서는 백틱 `` ` `` 사용. 한 줄로 써도 됩니다.)

`msbuild`가 PATH에 있다면 `msbuild NavisTreeExporter.sln /p:Configuration=Release /p:Platform=x64` 로도 동일하게 빌드됩니다.

## 5. 빌드 결과 확인

성공하면 아래 경로에 결과물이 생성됩니다.

```
src\NavisTreeExporter\bin\Release\net48\NavisTreeExporter.dll
src\NavisTreeExporter\bin\Release\net48\Newtonsoft.Json.dll
```

이 두 파일이 실제로 Navisworks에 설치할 대상입니다. 설치 방법은
[DEPLOY.md](./DEPLOY.md)를 참고하세요.

## 6. 자주 발생하는 오류

| 오류 메시지 | 원인 / 해결 |
|---|---|
| `Autodesk.Navisworks.Api을(를) 찾을 수 없습니다` / `HintPath` 관련 오류 | Navisworks 설치 경로가 다름 → 3-B 참고 |
| `NETSDK1045: 현재 .NET SDK가 ... net48을 지원하지 않습니다` | .NET Framework 4.8 Targeting Pack 미설치, 또는 .NET SDK가 너무 오래됨 → 0번 준비물 다시 설치 |
| NuGet 관련 오류 (`Newtonsoft.Json`을 찾을 수 없음) | 인터넷 연결 확인, 또는 VS에서 도구 → NuGet 패키지 관리자 → 패키지 소스에 `nuget.org` 등록 확인 |
| `RibbonTab`/`RibbonGroup`/`Command` 특성 관련 컴파일 오류 | 이 코드는 Navisworks SDK 없이 작성되어 검증되지 않았습니다. 오류 메시지를 알려주시면 실제 API 시그니처에 맞게 수정하겠습니다 (README의 "알려진 제약사항" 참고) |
| 플랫폼 불일치(x86/AnyCPU 관련) 오류 | 솔루션 구성이 x64인지 확인 (Navisworks는 64비트 전용) |

빌드 중 위 표에 없는 오류가 나오면, 오류 메시지 전체를 그대로 공유해 주세요.

## 참고

- 이 저장소는 원격(Linux) 개발 환경에서 소스 코드가 작성되어, 실제 컴파일은
  아직 검증되지 않았습니다. Windows에서 처음 빌드할 때 오류가 날 수 있으니
  나오는 오류 메시지를 그대로 알려주시면 바로 수정하겠습니다.
