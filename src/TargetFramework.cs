// Windows Audio Keys - 대상 프레임워크 선언(앱과 테스트가 함께 컴파일한다).
//
// build.ps1 은 SDK 없이 in-box csc.exe 로 빌드하므로, MSBuild 가 자동으로 넣어 주는
// TargetFrameworkAttribute 가 exe 에 없다. 이 특성이 없으면 .NET Framework 는 앱을
// "4.0 대상"으로 보고 4.5 이후의 모든 호환 동작(quirk)을 예전 방식으로 둔다.
// 그 결과 app.config 의 DpiAwareness=PerMonitorV2 가 무시되어 설정 창이 DPI 인식 없이
// (비트맵 확대로 흐리게) 표시됐다. WinForms 고DPI 기능은 4.7 이상 대상이어야 켜진다.
//
// 함께 바뀌는 4.8 기본 동작: 새 경로 정규화(긴 경로 허용), 강한 암호화·OS 기본 TLS,
// WinForms 접근성 개선(4.7.1~4.8). 테스트 exe 도 같은 파일을 컴파일해 같은 기준으로 검증한다.
// Windows 10 이상 호환 선언(app.manifest 의 supportedOS)도 함께 있어야 PerMonitorV2 가 된다.

[assembly: System.Runtime.Versioning.TargetFramework(
    ".NETFramework,Version=v4.8",
    FrameworkDisplayName = ".NET Framework 4.8")]
