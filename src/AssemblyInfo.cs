// Windows Audio Keys - 어셈블리 정보와 제품 버전의 단일 원본.
// 릴리스 버전은 이 파일에서만 바꾼다. 사용자에게 보이는 버전(패키지 파일명, 설정 창 제목)은
// AssemblyInformationalVersion 의 "주.부.수정" 값을 쓰고,
// AssemblyVersion/FileVersion 은 같은 값 뒤에 ".0" 을 붙여 함께 유지한다.

using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Windows Audio Keys")]
[assembly: AssemblyProduct("Windows Audio Keys")]
[assembly: AssemblyDescription("포그라운드 앱 오디오 단축키 유틸리티")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyCopyright("Copyright (c) 2026 devbattery")]

// 릴리스 버전: AssemblyInformationalVersion 을 기준으로 나머지 두 값을 맞춘다.
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

// COM 노출은 쓰지 않는다.
[assembly: ComVisible(false)]
