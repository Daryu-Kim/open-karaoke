# 오픈 노래방 (OpenKaraoke)

TJ 노래방 A 시리즈 UI를 참고해 만든 매장용 MR 반주 플레이어입니다.
YouTube에서 MR(반주) 영상을 검색해 내려받고, **키(±6 반음)** 와 **템포(80~120%)** 를 실시간으로 조절해 재생합니다.
예약(대기곡) 큐, 자동 다음 곡 재생, 이전/다음 곡 이동을 지원하며 화면의 모든 문구는 한국어입니다.

| 항목 | 내용 |
| --- | --- |
| 지원 OS | Windows 10/11 (x64), Linux (x64, X11/Wayland) |
| UI 스택 | C# .NET 8 + Avalonia (Windows/Linux 단일 코드베이스) |
| Windows 재생 | Media Foundation 디코딩 + WASAPI/WaveOut 출력 (NAudio) |
| Linux 재생 | ffmpeg 디코딩 → SoundTouch(키/템포) → OpenAL Soft 출력 |

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

`artifacts/` 폴더는 git에 커밋되지 않습니다. 완료 후 실행 파일 옆에 `THIRD-PARTY-NOTICES.md`, `README.md`
(리눅스는 `open-karaoke.desktop.in`, `install-desktop-entry.sh` 포함)가 함께 복사됩니다.

## 3. 실행에 필요한 외부 프로그램

| 용도 | Windows | Linux | 없을 때 |
| --- | --- | --- | --- |
| yt-dlp (MR 다운로드) | 실행 파일 옆 `yt-dlp.exe` 또는 PATH | 실행 파일 옆 `yt-dlp` 또는 PATH | 검색은 되지만 다운로드가 실패합니다 |
| ffmpeg (오디오 디코딩) | 재생에는 불필요 | **필수** | 재생이 실패합니다 |
| 한글 폰트 | 기본 내장 | `fonts-noto-cjk` 권장 | 글자가 네모(□□)로 보입니다 |

Ubuntu/Debian 설치 예:

```bash
sudo apt update
sudo apt install -y ffmpeg fonts-noto-cjk
sudo apt install -y yt-dlp          # 또는: python3 -m pip install -U yt-dlp

# 최소 설치(minimal) 배포판에서 화면/소리가 안 나올 때
sudo apt install -y libx11-6 libice6 libsm6 libfontconfig1 libgl1 libasound2
```

## 4. 설정

- **YouTube Data API 키** (검색 기능에 필요)
  - 환경 변수: `OPEN_KARAOKE_YOUTUBE_API_KEY`
  - 또는 실행 파일 옆 `appsettings.local.json`:
    ```json
    { "Youtube": { "ApiKey": "발급받은_API_키" } }
    ```
- **ffmpeg 경로 강제 지정** (자동 탐색 실패 시): `OPENKARAOKE_FFMPEG=/opt/ffmpeg/bin/ffmpeg`
- 자동 탐색 순서: 환경 변수 → 실행 파일 옆 → `PATH` → `/usr/bin`, `/usr/local/bin`, `/bin`, `/snap/bin`,
  `/var/lib/flatpak/exports/bin`, `/home/linuxbrew/.linuxbrew/bin`

## 5. 데이터 위치 (실행 파일 기준)

- `data/songs.db` — 곡 목록(제목/가수/TJ 번호/파일 경로)
- `data/songs/` — 내려받은 MR 오디오 파일

> 리눅스 설치 폴더는 사용자 쓰기 권한이 있어야 합니다. `/opt` 같은 곳에 두면 `data` 폴더를 만들지 못해
> 목록이 저장되지 않습니다. 예: `~/open-karaoke` 에 두는 것을 권장합니다.

## 6. 리눅스 PC 설치 순서 (배포본 기준)

1. `artifacts/linux-x64` **폴더 전체**를 리눅스 PC의 쓰기 가능한 위치로 복사 (예: `~/open-karaoke`)
2. 실행 권한 부여: `chmod +x OpenKaraoke install-desktop-entry.sh`
3. 실행: `./OpenKaraoke` (첫 실행 시 `data` 폴더가 만들어집니다)
4. 앱 메뉴에 등록하려면: `./install-desktop-entry.sh`
5. TJ 믹서/스피커를 시스템 기본 출력 장치로 지정한 뒤 실행하세요.

## 7. 문제 해결

| 증상 | 원인과 해결 |
| --- | --- |
| 곡 제목 아래에 "ffmpeg를 찾을 수 없습니다" 등 실패 사유가 표시됨 | 리눅스에 ffmpeg 미설치 → `sudo apt install ffmpeg` 후 재실행 |
| 다운로드 버튼에서 "yt-dlp가 없습니다" | 실행 파일 옆에 `yt-dlp`(Windows는 `yt-dlp.exe`)를 두거나 PATH에 설치 |
| 검색 결과가 비어 있음 | YouTube API 키 미설정, 할당량 초과, 네트워크 오류 |
| 화면 글자가 네모(□□)로 보임 | 한글 폰트 미설치 → `sudo apt install fonts-noto-cjk` |
| 소리가 나지 않음 | 시스템 기본 출력 장치 확인 (오디오는 항상 기본 장치로 출력됩니다) |
| 리눅스에서 창이 뜨지 않음 | `libx11-6 libice6 libsm6 libfontconfig1 libgl1` 설치 여부 확인 (최소 설치 배포판에서 발생) |
| 꺼졌다 켜면 목록이 사라짐 | `data` 폴더에 쓸 수 없는 위치 → 쓰기 가능한 폴더로 이동 |

## 8. 라이선스

- 서드파티 고지: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
  (SoundTouch LGPL-2.1, OpenAL Soft LGPL-2.0-or-later, Avalonia MIT, Silk.NET MIT, NAudio MIT 등)
- `ffmpeg`, `yt-dlp`는 번들에 포함하지 않으며 각 프로젝트의 라이선스를 따릅니다.
- YouTube 영상 다운로드는 각 영상의 저작권과 YouTube 서비스 약관을 확인한 뒤 사용하세요.
