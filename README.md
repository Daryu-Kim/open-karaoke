# 오픈 노래방 (OpenKaraoke)

TJ 노래방 A 시리즈 UI를 참고해 만든 매장용 MR 반주 플레이어입니다.
YouTube에서 MR(반주) 영상을 검색해 내려받고, **키(±6 반음)** 와 **템포(80~120%)** 를 실시간으로 조절해 재생합니다.
가사 **영상까지 손님용 모니터에 크게** 띄울 수 있고, 예약(대기곡) 큐, 자동 다음 곡 재생, 이전/다음 곡 이동을 지원합니다.
화면의 모든 문구는 한국어입니다.

| 항목 | 내용 |
| --- | --- |
| 지원 OS | Windows 10/11 (x64), Linux (x64, X11/Wayland) |
| UI 스택 | C# .NET 8 + Avalonia (Windows/Linux 단일 코드베이스) |
| Windows 재생 | Media Foundation 디코딩 + WASAPI/WaveOut 출력 (NAudio) |
| Linux 재생 | ffmpeg 디코딩 → SoundTouch(키/템포) → OpenAL Soft 출력 |
| 가사 화면 | 내려받은 1080p MP4 영상을 ffmpeg로 디코딩해 앱 창/보조 모니터에 표시 |

## 1. 개발 실행

```bash
# 저장소 루트에서
dotnet run --project src/OpenKaraoke.Desktop
```

테스트:

```bash
dotnet test src/OpenKaraoke.App.Tests
```

## 2. 배포본 만들기 (self-contained: 대상 PC에 .NET 설치 불필요)

```powershell
# Windows용 번들 → artifacts\win-x64
pwsh scripts/publish.ps1 -Runtime win-x64

# Linux용 번들 (Windows에서 교차 빌드) → artifacts\linux-x64
pwsh scripts/publish.ps1 -Runtime linux-x64
```

```bash
# Linux에서 직접 빌드할 때
scripts/publish-linux.sh            # 결과: artifacts/linux-x64
```

`artifacts/` 폴더는 git에 커밋되지 않습니다. 완료 후 실행 파일 옆에 `THIRD-PARTY-NOTICES.md`, `README.md`,
`LINUX.md`(리눅스는 `open-karaoke.desktop.in`, `install-desktop-entry.sh` 포함)가 함께 복사됩니다.

## 3. 실행에 필요한 외부 프로그램

| 용도 | Windows | Linux | 없을 때 |
| --- | --- | --- | --- |
| yt-dlp (MR 다운로드) | 실행 파일 옆 `yt-dlp.exe`, PATH, ⚙ 설정에서 경로 지정 | 실행 파일 옆 `yt-dlp`, PATH, ⚙ 설정에서 경로 지정 | 검색은 되지만 다운로드가 실패합니다 |
| ffmpeg (오디오 디코딩·가사 영상) | **필수** — PATH 또는 ⚙ 설정에서 경로 지정 | **필수** — PATH 또는 ⚙ 설정에서 경로 지정 | 재생과 가사 화면이 실패합니다 |
| 한글 폰트 | 기본 내장 | `fonts-noto-cjk` 권장 | 글자가 네모(□□)로 보입니다 |

> **앱에서 바로 설치할 수 있습니다.** 필요한 도구가 없으면 실행할 때 **"필요한 도구 설치"** 창이 떠서
> 공식 빌드(yt-dlp 약 20MB, ffmpeg 약 100MB)를 **실행 파일 옆에 자동으로 내려받습니다**(관리자 권한 불필요).
> 설치하지 않으려면 **나중에**를 누르거나 **다시 묻지 않기**를 선택하면 됩니다. yt-dlp 와 ffmpeg 는 모두
> **필수** 항목이라 체크 없이 자동으로 내려받습니다(체크 상자는 선택 항목에만 표시됩니다).
> 같은 창은 **⚙ 설정 → 외부 도구 경로 → 누락 도구 자동 설치**에서 언제든 열 수 있습니다.
> (리눅스에서 ffmpeg를 앱으로 설치한 경우에는 앱을 다시 시작한 뒤부터 재생에 적용됩니다.)

Ubuntu/Debian 설치 예:

```bash
sudo apt update
sudo apt install -y ffmpeg fonts-noto-cjk
sudo apt install -y yt-dlp          # 또는: python3 -m pip install -U yt-dlp

# 최소 설치(minimal) 배포판에서 화면/소리가 안 나올 때
sudo apt install -y libx11-6 libice6 libsm6 libfontconfig1 libgl1 libasound2
```

## 4. 사용 방법 (가사 화면 · 키/템포)

1. **검색** 탭에서 곡을 찾아 **다운로드** — 영상+음성이 합쳐진 **1080p MP4**로 저장됩니다(곡당 수십 초~수 분).
2. 곡을 누르면 재생이 시작되고, **손님용 화면**에 가사 영상이 표시됩니다.
   - **보조 모니터(고객용 TV)가 연결되어 있으면** 그 모니터에 테두리 없는 전체화면 창으로 자동 표시됩니다.
   - 모니터가 하나뿐이면 앱 창의 **노래방 화면** 탭에 크게 표시됩니다.
3. 화면 상단 탭: **내 라이브러리 / 검색 / 노래방 화면**. **F 키**로 앱 창 전체화면(노래방 화면)을 켜고 끌 수 있습니다.
4. 키(♯/♭, ±6 반음)와 템포(80~120%)는 오른쪽 아래 컨트롤에서 조절합니다. 조절해도 영상은 소리와 항상 맞춰 재생됩니다.
5. 예전에 받아 둔 **오디오 전용 곡**은 목록 오른쪽의 **영상 다시 받기** 버튼으로 다시 받으면 가사 화면이 나옵니다
   (제목·가수·TJ 번호는 그대로 유지되고, 기존 오디오 파일은 자동으로 정리됩니다).

> 영상 처리는 재생 중에만 ffmpeg 프로세스 하나를 사용합니다. 곡이 끝나거나 멈추면 디코딩이 즉시 중단됩니다.
> 보조 모니터를 쓰면 다른 탭에서 다음 곡을 고르는 동안에도 고객용 화면에는 가사가 계속 표시됩니다.
> 모니터가 하나뿐일 때는 **노래방 화면** 탭을 벗어나면 영상 처리가 멈추고 소리만 나옵니다(저사양 PC 대비).

## 5. 설정

앱 오른쪽 위 **⚙ 설정** 버튼에서 입력하고 **저장**하면 실행 파일 옆 `appsettings.local.json` 에 기록됩니다.
FFmpeg·yt-dlp 경로는 다음 작업부터, API 키는 다음 검색부터 적용되므로 재실행이 필요 없습니다.

- **YouTube Data API 키** (검색 기능에 필요) — Google Cloud Console에서 YouTube Data API v3 사용 설정 후 발급
- **ffmpeg / yt-dlp 경로** — `찾아보기`로 선택하거나 직접 입력, **비워 두면 자동 탐색**
- 자동 탐색 순서: 설정 값 → 실행 파일 옆 → `PATH` → `/usr/bin`, `/usr/local/bin`, `/bin`, `/snap/bin`,
  `/var/lib/flatpak/exports/bin`, `/home/linuxbrew/.linuxbrew/bin`
- 환경 변수로도 지정할 수 있으며, **환경 변수 값이 설정 화면에 저장한 값보다 우선**합니다.

  | 대상 | 환경 변수 |
  | --- | --- |
  | YouTube API 키 | `OPEN_KARAOKE_YOUTUBE_API_KEY` |
  | ffmpeg | `OPENKARAOKE_FFMPEG` |
  | yt-dlp | `OPENKARAOKE_YTDLP` |

`appsettings.local.json` 직접 편집도 가능합니다(설정 화면은 나머지 항목을 그대로 유지합니다).

```json
{
  "Youtube": { "ApiKey": "발급받은_API_키" },
  "Tools": { "FfmpegPath": "/usr/bin/ffmpeg", "YtDlpPath": "/usr/local/bin/yt-dlp" }
}
```

> 이 파일은 `.gitignore` 에 등록되어 있어 저장소에 올라가지 않습니다. API 키가 들어가므로 공유하지 마세요.

## 6. 데이터 위치 (실행 파일 기준)

- `appsettings.local.json` — 설정 화면에서 저장한 API 키와 도구 경로 (선택)
- `data/songs.db` — 곡 목록(제목/가수/TJ 번호/파일 경로)
- `data/songs/` — 내려받은 MR 파일 (1080p MP4: 곡당 약 100~200MB, 예전 곡은 오디오 전용 m4a/mp3)
- `logs/app.log` — 실행 기록과 오류 내용 (2MB를 넘으면 `logs/app.log.1`로 한 번만 보관)

> 가사 영상까지 저장하면 용량이 커집니다. 곡 100곡 기준 대략 10~20GB를 확보하세요.

> 리눅스 설치 폴더는 사용자 쓰기 권한이 있어야 합니다. `/opt` 같은 곳에 두면 `data` 폴더를 만들지 못해
> 목록이 저장되지 않습니다. 예: `~/open-karaoke` 에 두는 것을 권장합니다.

## 7. 리눅스 PC 설치 순서 (배포본 기준)

1. `artifacts/linux-x64` **폴더 전체**를 리눅스 PC의 쓰기 가능한 위치로 복사 (예: `~/open-karaoke`)
2. 실행 권한 부여: `chmod +x OpenKaraoke install-desktop-entry.sh`
3. 실행: `./OpenKaraoke` (첫 실행 시 `data` 폴더가 만들어집니다)
4. 앱 메뉴에 등록하려면: `./install-desktop-entry.sh`
5. TJ 믹서/스피커를 시스템 기본 출력 장치로 지정한 뒤 실행하세요.

> 설치 파일 옮기는 방법, 오디오 설정, 자동 실행, 업데이트/제거까지 자세한 내용은
> **[LINUX.md](LINUX.md) (리눅스 설치·운영 안내)** 를 참고하세요.

## 8. 문제 해결

| 증상 | 원인과 해결 |
| --- | --- |
| 곡 제목 아래에 "ffmpeg를 찾을 수 없습니다" 등 실패 사유가 표시됨 | 리눅스에 ffmpeg 미설치 → `sudo apt install ffmpeg` 후 재실행 |
| 다운로드 버튼에서 "yt-dlp가 없습니다" | 실행 파일 옆에 `yt-dlp`(Windows는 `yt-dlp.exe`)를 두거나 PATH에 설치 |
| 검색 결과가 비어 있음 | YouTube API 키 미설정, 할당량 초과, 네트워크 오류 → ⚙ 설정에서 키 저장 |
| 설정에서 저장했는데 적용되지 않음 | 환경 변수(`OPEN_KARAOKE_YOUTUBE_API_KEY`, `OPENKARAOKE_FFMPEG`, `OPENKARAOKE_YTDLP`)가 설정되어 있으면 그 값이 우선합니다 → 환경 변수 해제 후 재실행 |
| 설정 화면의 `찾아보기`가 열리지 않음 | 일부 리눅스 세션에 파일 선택 포털이 없을 때 발생 → 경로를 직접 입력해 저장 |
| 화면 글자가 네모(□□)로 보임 | 한글 폰트 미설치 → `sudo apt install fonts-noto-cjk` |
| 소리가 나지 않음 | 시스템 기본 출력 장치 확인 (오디오는 항상 기본 장치로 출력됩니다) |
| 리눅스에서 창이 뜨지 않음 | `libx11-6 libice6 libsm6 libfontconfig1 libgl1` 설치 여부 확인 (최소 설치 배포판에서 발생) |
| 꺼졌다 켜면 목록이 사라짐 | `data` 폴더에 쓸 수 없는 위치 → 쓰기 가능한 폴더로 이동 |
| 노래방 화면에 영상이 안 나옴(검은 화면) | 예전에 받은 오디오 전용 곡입니다 → 목록에서 **영상 다시 받기** 실행. ffmpeg 미설치여도 같은 증상이 나오므로 ⚙ 설정에서 경로를 확인하세요 |
| 보조 모니터 대신 앱 창에만 영상이 나옴 | 고객용 TV가 연결/인식되지 않은 상태입니다 → 케이블과 **디스플레이 설정 → 확장** 여부를 확인한 뒤 곡을 다시 시작하세요 |
| 고객용 화면이 잘리거나 여백이 큼 | TV의 화면비(16:9)와 PC 해상도 설정을 확인하세요. 영상은 원본 비율을 유지하며 남는 부분은 검게 표시됩니다 |
| 영상이 끊기거나 버벅임 | CPU 사용률을 확인하고, 다른 작업을 줄이세요. 필요하면 곡을 낮은 화질로 다시 받아 저장하세요 |
| 실행 중 오류가 나거나 갑자기 종료됨 | 실행 파일 옆 `logs/app.log`를 열어 마지막 줄의 오류 내용 확인 (시작 시 버전·OS·ffmpeg·yt-dlp·API 키 상태가 함께 기록됩니다) |

## 9. 라이선스

- 서드파티 고지: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
  (SoundTouch LGPL-2.1, OpenAL Soft LGPL-2.0-or-later, Avalonia MIT, Silk.NET MIT, NAudio MIT 등)
- `ffmpeg`, `yt-dlp`는 번들에 포함하지 않으며 각 프로젝트의 라이선스를 따릅니다.
- YouTube 영상 다운로드는 각 영상의 저작권과 YouTube 서비스 약관을 확인한 뒤 사용하세요.
