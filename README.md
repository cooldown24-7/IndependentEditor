# 독립 2D 편집기

JSON·SKEL 스켈레톤과 Atlas를 열어 메시를 편집하고 애니메이션을 확인하는 Windows용 .NET 10 WPF 앱입니다.

## 주요 기능

- 본·슬롯·파츠 계층 탐색, 스킨 전환, 항목별 선택·표시 제어
- 메시 파츠 추가·삭제, 정점 다중 선택·이동·추가·삭제, 외곽 편집
- 슬롯·파츠 색상 편집과 Undo/Redo
- 설정·애니메이션 모드 전환, 애니메이션 재생·반복·시각 이동, 지원되는 Deform 키 편집
- 캔버스 확대·이동·격자 표시와 PNG·Atlas 텍스처 미리보기
- JSON 묶음 사본 저장, 지원 SKEL 원본·사본 저장과 최초 원본 백업

SKEL은 3.8.55·4.1.11·4.1.17의 지원 프로필을 대상으로 하며, 파일에 따라 재생·편집 범위가 제한됩니다. 본 생성·가중치 작성·범용 키/곡선 편집 UI는 제공하지 않습니다.

## 실행

Windows x64용입니다. GitHub Releases에 첨부된 실행용 ZIP 전체를 쓰기 가능한 폴더에 압축 해제하고 `IndependentEditor.exe`를 더블클릭하세요. DLL과 .NET 런타임은 EXE에 포함되어 별도로 설치할 필요가 없습니다. 함께 제공된 `fixtures`·`licenses` 폴더와 README·LICENSE는 유지하세요. 실행에 필요한 네이티브 파일은 Windows 임시 폴더에 자동으로 풀립니다.

포함된 예제가 열리면 상단 `열기`로 자신의 JSON·SKEL을 선택하세요. 같은 이름의 Atlas가 없으면 연결할 Atlas를 선택하는 창이 열립니다. Atlas가 참조하는 PNG도 필요합니다.

JSON은 Atlas·PNG를 포함한 사본으로 저장합니다. SKEL의 `저장`은 현재 파일을 갱신하며 최초 덮어쓰기 전에 `.skel.bak` 백업을 만듭니다. `다른 이름`으로 저장한 뒤에는 그 사본을 갱신합니다. JSON 저장과 SKEL 사본 저장은 실행 폴더의 `output` 아래로 제한됩니다.

## 소스 빌드

GitHub의 `Source code (zip)`과 소스 저장소에는 실행 파일이 포함되지 않습니다. 로컬에서 패키지를 만든 경우에는 프로젝트 루트의 `배포/IndependentEditor.exe`로 실행합니다. `배포`, `output`, `.local` 폴더는 Git에서 제외됩니다.

소스에서 빌드하려면 Windows x64와 .NET 10 SDK가 필요합니다. 프로젝트 루트에서 실행하세요.

```powershell
dotnet build src/IndependentDev.csproj -c Debug --artifacts-path output/build --configfile NuGet.Config
dotnet output/build/bin/IndependentDev/debug/IndependentEditor.dll --editor-skia fixtures/preview/scene.json fixtures/preview/scene.atlas
```

CLI로 지원 SKEL을 열려면 위 DLL 경로 뒤에 `--editor-skel-setup41 input.skel input.atlas`를 지정하세요. VS Code에서는 C# 디버거를 설치하고 F5로 합성 예제를 실행할 수 있습니다.

런타임을 포함한 EXE 배포 폴더는 다음 명령으로 만듭니다. 최초 게시에는 Microsoft 런타임 다운로드를 위한 인터넷 연결이 필요합니다.

```powershell
dotnet publish src/IndependentDev.csproj -c Release -r win-x64 --self-contained true --artifacts-path output/build -o 배포 -p:DebugType=None --source https://api.nuget.org/v3/index.json
```

## 라이선스

자체 코드와 합성 예제는 [MIT](LICENSE) 라이선스입니다. 소스의 SkiaSharp 3.119.4 고지는 `vendor/SkiaSharp/3.119.4`에 있습니다. 실행용 배포본에는 SkiaSharp와 Microsoft .NET 런타임의 고지가 `licenses` 아래에 포함되어 있습니다. 재배포 시 `LICENSE`와 `licenses` 전체를 함께 제공하세요.

Esoteric Software의 공식 제품이 아닙니다. 모든 Spine 버전·기능과의 호환성을 보장하지 않으며, 사용자가 여는 자산의 이용 권한은 사용자가 확보해야 합니다.
