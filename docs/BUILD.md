# 빌드 방법

이 애드인은 **Windows + Navisworks Manage 2022 (.NET Framework 4.8)** 환경에서만
빌드/실행할 수 있습니다. Navisworks API DLL은 Autodesk SDK 라이선스 정책상
저장소에 포함하지 않으므로, 로컬에 설치된 Navisworks의 DLL을 참조합니다.

## 사전 준비

1. Windows에 Navisworks Manage 2022 설치 (API DLL이 설치 폴더에 포함되어 있음)
   - 기본 경로: `C:\Program Files\Autodesk\Navisworks Manage 2022\`
2. Visual Studio 2022 (.NET desktop development workload) 또는 `dotnet` CLI + MSBuild
3. NuGet 패키지 복원 가능한 인터넷 연결 (Newtonsoft.Json)

## 빌드

Navisworks가 기본 경로에 설치되어 있다면 바로 빌드됩니다.

```powershell
dotnet build NavisTreeExporter.sln -c Release
```

다른 경로에 설치되어 있다면 `NavisworksInstallDir` 속성을 지정합니다.

```powershell
dotnet build NavisTreeExporter.sln -c Release ^
  /p:NavisworksInstallDir="D:\Autodesk\Navisworks Manage 2022\"
```

빌드 결과물은 `src\NavisTreeExporter\bin\Release\net48\` 아래에 생성됩니다.
(`NavisTreeExporter.dll`, `Newtonsoft.Json.dll` 등)

## 참고

- 이 저장소를 원격(Linux) 환경에서 개발하는 경우, 소스 코드 작성/리뷰까지만
  가능하고 실제 컴파일·Navisworks 실행 테스트는 Windows 환경에서 직접
  수행해야 합니다.
