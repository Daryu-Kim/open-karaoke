# 서드파티 고지 / Third-Party Notices

이 문서는 OpenKaraoke(오픈 노래방) 애플리케이션이 사용하는 오픈소스 구성 요소와
라이선스를 정리한 것입니다. 배포본에는 이 파일이 함께 포함됩니다.

This file lists the open-source components used by OpenKaraoke and their licenses.
The file is shipped with every published build.

## 배포본에 포함되는 구성 요소 (Bundled)

| 구성 요소 (Component) | 버전 (Version) | 라이선스 (License) | 용도 (Purpose) |
| --- | --- | --- | --- |
| [SoundTouch.Net](https://github.com/owoudenberg/soundtouch.net) | 2.3.2 | LGPL-2.1 | 피치(키)·템포 실시간 변환 DSP |
| [OpenAL Soft](https://github.com/kcat/openal-soft) (via Silk.NET.OpenAL.Soft.Native) | 1.23.1 | LGPL-2.0-or-later | Linux/macOS/Windows 오디오 출력 (`libopenal.so`, `soft_oal.dll`) |
| [Silk.NET.OpenAL](https://github.com/dotnet/Silk.NET) | 2.23.0 | MIT | OpenAL 바인딩 |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | 11.3.21 | MIT | 크로스플랫폼 데스크톱 UI (Windows/Linux 단일 코드베이스) |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MIT | MVVM 인프라 |
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | MIT | Windows (WASAPI) 오디오 출력 |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | 8.0.10 | MIT | 곡 라이브러리 SQLite 접근 |
| [SQLitePCLRaw.bundle_e_sqlite3](https://github.com/ericsink/SQLitePCL.raw) | 2.1.6 | Apache-2.0 | 네이티브 SQLite 제공 (SQLite 자체는 Public Domain) |
| [SkiaSharp](https://github.com/mono/SkiaSharp) / [HarfBuzzSharp](https://github.com/mono/SkiaSharp) | Skia 2.88 계열 | MIT | 텍스트 렌더링·폰트 대체 (Avalonia 의존성, 네이티브 Skia/HarfBuzz 포함) |

### LGPL 구성 요소 안내

* SoundTouch.Net과 OpenAL Soft는 **수정하지 않은 원본 바이너리**를 동적 링크로 사용합니다.
  배포본에는 소스 코드를 함께 제공해야 하는 의무가 발생하지 않지만, 사용자는 해당 라이브러리를
  교체(재링크)할 수 있습니다.
* 원본 소스: SoundTouch.Net <https://github.com/owoudenberg/soundtouch.net>,
  OpenAL Soft <https://github.com/kcat/openal-soft>
* 각 라이브러리는 `bin/`(또는 `libopenal.so`) 형태로 배포본 루트에 함께 들어 있습니다.
  동일 버전의 다른 빌드로 교체해도 애플리케이션은 그대로 동작합니다.

## 배포본에 포함되지 않는 구성 요소 (External tools)

아래 도구는 **번들하지 않고 시스템에서 찾아 실행**합니다(설치 방법은 `README.md` 참고).
따라서 각 도구의 라이선스는 사용자가 설치한 배포판의 조건을 따릅니다.

| 구성 요소 (Component) | 라이선스 (License) | 용도 (Purpose) |
| --- | --- | --- |
| ffmpeg / ffprobe | LGPL-2.1-or-later (일부 빌드는 GPL) | MR 음원 디코딩 (Linux 재생 경로), 다운로드 후 변환 |
| yt-dlp | Unlicense | YouTube MR 영상 다운로드 |

## 개발 전용 (Development only, 배포본 미포함)

xunit, Microsoft.NET.Test.Sdk, coverlet.collector — 테스트 프로젝트에서만 사용됩니다 (MIT/Apache-2.0).
