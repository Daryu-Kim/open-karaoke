# 리눅스 설치·운영 안내 (OpenKaraoke)

TJ 믹서/스피커가 연결된 매장용 리눅스 PC에 **오픈 노래방** 배포본을 설치하고 운영하는 전체 절차입니다.
빠른 요약은 [README.md](README.md) 7장에 있고, 이 문서는 **설치 → 설정 → 오디오 → 자동 실행 → 업데이트** 순서로 더 자세히 설명합니다.

## 0. 한눈에 보기

| 항목 | 내용 |
| --- | --- |
| 대상 하드웨어 | **x86_64(amd64) 전용** — ARM(aarch64)은 지원하지 않습니다 |
| 대상 OS | Ubuntu 22.04/24.04, Debian 12, Linux Mint, Fedora 38+ 등 (데스크톱 환경 필요) |
| 배포본 | `artifacts/linux-x64` 폴더 전체 (자체 포함 빌드 — .NET 설치 불필요) |
| 사전 설치 | `ffmpeg` (필수), `fonts-noto-cjk` (한글), `yt-dlp` — **ffmpeg/yt-dlp 는 앱에서 자동 설치 가능**(3장) |
| 디스크 사용량 | 약 400MB (배포본) + **곡당 약 100~200MB** (1080p 가사 영상) |
| 소리 출력 | 시스템 **기본 출력 장치**로만 재생됩니다 (앱에 장치 선택 기능 없음) |
| 가사 화면 | 보조 모니터(고객용 TV)에 전체화면 창으로 자동 표시, 없으면 앱 창 안에 크게 표시 |

설치 요약 7단계:

```bash
# 1) 배포본 복사 (예: ~/open-karaoke)
# 2) 필수 패키지
sudo apt update && sudo apt install -y ffmpeg fonts-noto-cjk
# 3) yt-dlp 설치 (4장 참고) — 앱을 실행하면 자동 설치 창으로 대신할 수 있습니다
# 4) 실행 권한 + 첫 실행
cd ~/open-karaoke && chmod +x OpenKaraoke install-desktop-entry.sh && ./OpenKaraoke
# 5) 자동 설치 창이 뜨면 "지금 설치" → ffmpeg/yt-dlp 내려받기 (ffmpeg는 앱 재시작 후 적용)
# 6) ⚙ 설정에서 YouTube API 키 입력 후 저장
# 7) TJ 믹서를 기본 출력 장치로 지정, 필요 시 ./install-desktop-entry.sh
```

> **고객용 TV(보조 모니터)를 쓸 때**: TV를 PC에 연결하고 **디스플레이 설정 → 확장**으로 두면,
> 곡을 재생할 때 가사 영상이 그 모니터에 전체화면으로 자동 표시됩니다(곡이 끝나면 창은 자동으로 닫힙니다).

## 1. 준비물

- **배포본 만들기** (개발 PC에서):

  ```powershell
  pwsh scripts\publish.ps1 -Runtime linux-x64     # Windows에서 교차 빌드 → artifacts\linux-x64
  ```

  ```bash
  scripts/publish-linux.sh                        # 리눅스에서 직접 빌드할 때 (→ artifacts/linux-x64)
  ```

- **YouTube Data API 키**: 없으면 검색이 동작하지 않습니다(재생/목록은 동작). Google Cloud Console에서
  *YouTube Data API v3* 를 사용 설정한 뒤 발급합니다.
- **인터넷 연결**: 곡 검색과 다운로드에 필요합니다.
- **오디오 출력**: TJ 믹서 또는 스피커 (3.5mm, USB 오디오 인터페이스, HDMI 등 무엇이든 인식만 되면 사용 가능).

## 2. 리눅스 PC로 배포본 복사

`artifacts/linux-x64` **폴더 전체**(약 90MB)를 리눅스 PC로 옮깁니다.
일부 파일만 복사하거나 하위 폴더를 건너뛰면 실행되지 않습니다(`libopenal.so`, `*.so` 등 포함 필요).

```bash
# (리눅스 PC에서) 설치 폴더 생성
mkdir -p ~/open-karaoke
```

USB 메모리로 옮길 때(Windows 기준):

```powershell
# USB 드라이브(예: E:\) 루트에 폴더째 복사
Copy-Item -Recurse -Force artifacts\linux-x64 E:\open-karaoke
```

네트워크로 옮길 때(Windows → 리눅스 PC):

```powershell
# 리눅스 PC에 SSH 서버(openssh-server)가 있을 때
# (리눅스 PC에서 먼저: mkdir -p ~/open-karaoke)
scp -r artifacts/linux-x64/* karaoke@192.168.0.20:~/open-karaoke/
```

USB/네트워크 복사 후 리눅스 PC에서:

```bash
cd ~/open-karaoke
ls -l OpenKaraoke            # 파일이 있어야 합니다
file OpenKaraoke             # "ELF 64-bit LSB executable, x86-64" 이어야 정상
chmod +x OpenKaraoke install-desktop-entry.sh
```

> **압축(zip)으로 옮겼다면** 실행 권한이 사라지므로 위 `chmod +x` 를 반드시 실행하세요.
> **설치 위치는 쓰기 가능한 폴더여야 합니다.** `/opt`, `/usr/local` 같은 곳에 두면 `data` 폴더를 만들지
> 못해 곡 목록이 저장되지 않습니다. (부득이 `/opt` 를 쓰려면 `sudo chown -R "$USER" /opt/open-karaoke`)

## 3. 필수 패키지 설치

### Ubuntu / Debian / Linux Mint

```bash
sudo apt update
sudo apt install -y ffmpeg fonts-noto-cjk

# 최소 설치(minimal) 배포판이거나 "창이 안 뜬다 / 소리가 안 난다"면
sudo apt install -y libx11-6 libice6 libsm6 libfontconfig1 libgl1 libasound2

# Wayland 세션에서 창이 뜨지 않을 때 (XWayland)
sudo apt install -y xwayland

# (선택) 오디오 진단 도구: pactl, alsamixer
sudo apt install -y pulseaudio-utils alsa-utils
```

### Fedora

```bash
sudo dnf install -y ffmpeg google-noto-sans-cjk-fonts alsa-utils
# ffmpeg는 RPM Fusion 저장소가 필요할 수 있습니다:
#   sudo dnf install -y https://download1.rpmfusion.org/free/fedora/rpmfusion-free-release-$(rpm -E %fedora).noarch.rpm
```

| 패키지 | 용도 | 없을 때 증상 |
| --- | --- | --- |
| `ffmpeg` | MP3/M4A/MP4 오디오 디코딩 (**필수**) | 재생 시 "ffmpeg를 찾을 수 없습니다" |
| `fonts-noto-cjk` | 한글 폰트 | 화면 글자가 네모(□□) |
| `libx11-6`, `libice6`, `libsm6`, `libfontconfig1`, `libgl1` | 창 표시/글꼴/그리기 | 앱이 시작되지 않음 |
| `libasound2` | ALSA 라이브러리 (OpenAL Soft가 사용) | 소리 없음 |
| `xwayland` | Wayland 세션에서 X11 앱 실행 | 창이 뜨지 않음 |

> OpenAL Soft 오디오 엔진(`libopenal.so`)은 **배포본에 포함**되어 있으므로 따로 설치할 필요가 없습니다.
> Python·.NET 도 설치할 필요가 없습니다(자체 포함 빌드).

### 앱 안에서 자동 설치 (sudo 없이)

`ffmpeg` 와 `yt-dlp` 가 없으면 앱을 켤 때 **"필요한 도구 설치"** 창이 자동으로 뜨고, **지금 설치**를 누르면
공식 빌드를 실행 파일 옆(`~/open-karaoke/ffmpeg`, `~/open-karaoke/yt-dlp`)에 내려받습니다.

- 관리자 권한(sudo)이 필요 없고, 시스템 패키지를 건드리지 않습니다.
- 내려받기 크기는 yt-dlp 약 20MB, ffmpeg 약 100MB이며 압축 해제에 `tar`/`xz-utils` 가 필요합니다
  (Ubuntu/Debian 기본 포함: `sudo apt install -y tar xz-utils`).
- **ffmpeg 는 앱을 다시 시작한 뒤부터** 재생에 적용됩니다(실행 중인 재생 엔진은 시작 시 ffmpeg 경로를 고정).
  yt-dlp 는 설치 즉시 다음 다운로드부터 사용됩니다.
- 다시 묻지 않게 하려면 창에서 **다음부터 이 알림을 표시하지 않습니다**를 체크하고 닫으면 됩니다.
  이후에도 **⚙ 설정 → 외부 도구 경로 → 누락 도구 자동 설치**로 직접 설치할 수 있습니다.
- 자동 설치 대신 배포판 패키지(`sudo apt install ffmpeg`)를 쓰는 편이 좋다면 위 3장 안내를 그대로 따르세요.
  앱은 **⚙ 설정 경로 → 실행 파일 옆 → PATH** 순으로 찾으므로 둘 다 있어도 충돌하지 않습니다
  (PATH보다 실행 파일 옆이 우선입니다).

## 4. yt-dlp 설치 (MR 다운로드용)

가장 간단한 방법은 **앱 안에서 자동 설치**(3장 "앱 안에서 자동 설치")입니다. 아래는 직접 설치하는 방법이며,
셋 중 **하나**만 하면 됩니다.

### 방법 A — 공식 바이너리를 앱 폴더에 넣기 (권장)

최신 버전을 계속 쓸 수 있고, YouTube 변경에 대응하는 업데이트가 빠릅니다.

```bash
cd ~/open-karaoke
curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux -o yt-dlp
chmod +x yt-dlp
./yt-dlp --version
```

- 파일 이름은 반드시 **`yt-dlp`** 로 저장하세요(`yt-dlp_linux` 그대로 두면 자동 탐색이 안 됩니다).
- 나중에 업데이트: `./yt-dlp -U` (앱 폴더/파일 소유자가 쓰기 가능하므로 `sudo` 는 필요 없습니다)

### 방법 B — pipx / pip 로 설치

```bash
sudo apt install -y pipx && pipx ensurepath
pipx install yt-dlp          # → ~/.local/bin/yt-dlp
# 이미 설치되어 있다면:  pipx upgrade yt-dlp
```

PATH에 `~/.local/bin` 이 포함되어 있어야 앱이 자동으로 찾습니다.

### 방법 C — 배포판 패키지

```bash
sudo apt install -y yt-dlp
```

설치는 간단하지만 **버전이 오래되어 다운로드가 실패하는 경우가 많습니다.** 다운로드 실패가 계속되면
방법 A로 교체하세요.

앱은 다음 순서로 yt-dlp를 찾습니다:
**⚙ 설정에 저장한 경로 → 실행 파일 옆(`~/open-karaoke/yt-dlp`) → `PATH` →
`/usr/bin`, `/usr/local/bin`, `/bin`, `/snap/bin`, `/var/lib/flatpak/exports/bin`, `/home/linuxbrew/.linuxbrew/bin`**

같은 방식으로 `ffmpeg` 도 찾습니다(자동 설치되는 `/usr/bin/ffmpeg` 는 그대로 잡힙니다).

## 5. 첫 실행과 상태 확인

```bash
cd ~/open-karaoke
./OpenKaraoke
```

첫 실행 시 실행 파일 옆에 `data/`, `logs/` 폴더가 만들어집니다. 별도 터미널에서 확인:

```bash
tail -n 20 ~/open-karaoke/logs/app.log
```

로그 첫 부분에 버전·OS·ffmpeg 경로·yt-dlp 경로·API 키 설정 여부가 기록됩니다.

체크리스트:

1. 창이 뜨고 한글이 정상 표시되는가 (깨지면 `fonts-noto-cjk`)
2. 첫 실행 시 **"필요한 도구 설치"** 창이 떴다면 **지금 설치**로 ffmpeg/yt-dlp 를 내려받았는가
   (ffmpeg 를 설치했다면 앱을 한 번 다시 시작 — 이후 항목은 재시작 후 확인)
3. ⚙ 설정 → **YouTube API 키** 입력 → 저장 → 상태 문구가 "저장되었습니다"인가
4. 검색창에 곡명 입력 → 결과가 나오는가 (키 미설정/할당량 초과 시 실패)
5. 결과에서 곡을 선택해 **다운로드** → 목록에 추가되고 재생되는가
6. 재생 시 **가사 영상**이 나오는가
   - 보조 모니터가 있으면 그쪽에 전체화면 창이 뜨는가
   - 없으면 **노래방 화면** 탭(또는 **F 키** 전체화면)에 크게 나오는가
   - 검은 화면이면 예전에 받은 오디오 전용 곡입니다 → 목록의 **영상 다시 받기** 실행
7. 키(±6 반음)·템포(80~120%)를 바꿔도 소리와 영상이 정상인가

> `설정` 창의 `찾아보기` 는 데스크톱 포털이 없는 최소 세션에서 열리지 않을 수 있습니다.
> 이때는 경로를 직접 입력하면 됩니다(예: `/usr/bin/ffmpeg`).

## 6. 설정 파일과 환경 변수

앱에서 저장한 값은 실행 파일 옆 `appsettings.local.json` 에 기록되고, **재실행 없이** 다음 작업부터 적용됩니다.

```json
{
  "Youtube": { "ApiKey": "발급받은_API_키" },
  "Tools": { "FfmpegPath": "/usr/bin/ffmpeg", "YtDlpPath": "/usr/local/bin/yt-dlp" }
}
```

| 대상 | 환경 변수 | 우선순위 |
| --- | --- | --- |
| YouTube API 키 | `OPEN_KARAOKE_YOUTUBE_API_KEY` | 환경 변수 > 설정 파일 |
| ffmpeg | `OPENKARAOKE_FFMPEG` | 환경 변수 > 설정 파일 |
| yt-dlp | `OPENKARAOKE_YTDLP` | 환경 변수 > 설정 파일 |

헤드리스/키오스크로 띄울 때는 실행 스크립트에서 지정하는 편이 편합니다.

```bash
#!/usr/bin/env sh
export OPEN_KARAOKE_YOUTUBE_API_KEY='발급받은_API_키'
export OPENKARAOKE_FFMPEG=/usr/bin/ffmpeg
exec "$HOME/open-karaoke/OpenKaraoke" "$@"
```

> `appsettings.local.json` 에는 API 키가 들어가므로 다른 사람과 공유하지 마세요. (저장소에는 커밋되지 않습니다)

## 7. 오디오: TJ 믹서로 소리 내기

앱은 **항상 시스템 기본 출력 장치**로 재생합니다. 따라서 **TJ 믹서(또는 스피커)를 기본 출력으로 지정**하는 것이
핵심입니다.

데스크톱 설정에서 지정(가장 확실):

- GNOME: `설정 → 소리 → 출력` 에서 TJ 믹서 선택
- KDE: `시스템 설정 → 오디오 → 장치` 에서 기본 장치로 지정

명령으로 지정:

```bash
# PulseAudio / PipeWire (pactl 사용 시)
pactl list short sinks                 # 사용 가능한 출력 목록과 이름 확인
pactl set-default-sink <출력_이름>      # 예: alsa_output.usb-...analog-stereo

# PipeWire (WirePlumber)
wpctl status                           # ID 확인
wpctl set-default <ID>                 # 기본 출력 지정
```

- 장치 목록(ALSA 기준)은 `aplay -l` 로도 확인할 수 있습니다.
- 재부팅 후 되돌아가면 **데스크톱 설정 화면에서 선택**하거나 위 명령을 자동 시작 스크립트에 넣으세요.
- 재생 중에 기본 장치를 바꾸면 소리가 멈출 수 있습니다. **정지 후 다시 재생**하세요.

무음/볼륨 확인:

```bash
alsamixer        # F6 으로 카드 선택, M 키로 음소거 해제, ↑↓ 로 볼륨
```

소리 진단(앱을 터미널에서 실행할 때):

```bash
ALSOFT_LOGLEVEL=3 ./OpenKaraoke                    # OpenAL Soft가 어떤 드라이버를 여는지 확인
ALSOFT_DRIVERS=pipewire ./OpenKaraoke              # PipeWire로 강제
ALSOFT_DRIVERS=pulse ./OpenKaraoke                 # PulseAudio로 강제
ALSOFT_DRIVERS=alsa ./OpenKaraoke                  # ALSA로 강제 (Pulse/PipeWire 미사용 시스템)
```

## 8. 앱 메뉴(런처) 등록

```bash
cd ~/open-karaoke
./install-desktop-entry.sh
```

- `install-desktop-entry.sh` 는 **배포본 폴더 안에서** 실행해야 합니다(스크립트 위치를 기준으로 경로를 기록합니다).
  실행 권한이 사라졌을 때도 이 스크립트가 `OpenKaraoke` 의 `chmod +x` 를 함께 처리합니다.
- 생성 위치는 `${XDG_DATA_HOME:-$HOME/.local/share}/applications/open-karaoke.desktop` 이고, 앱 목록에
  **오픈 노래방** 이 나타납니다.
- 설치 폴더를 옮겼다면 스크립트를 다시 실행해 경로를 갱신하세요.
- 제거:

```bash
rm -f "${XDG_DATA_HOME:-$HOME/.local/share}/applications/open-karaoke.desktop"
command -v update-desktop-database >/dev/null && update-desktop-database "${XDG_DATA_HOME:-$HOME/.local/share}/applications"
```

## 9. 매장용 자동 실행 (선택)

로그인하면 자동으로 실행되게 하려면 자동 시작 항목을 만듭니다(전체 화면은 실행 후 **F 키**).

```bash
mkdir -p ~/.config/autostart
cp "${XDG_DATA_HOME:-$HOME/.local/share}/applications/open-karaoke.desktop" \
   ~/.config/autostart/open-karaoke.desktop
printf 'X-GNOME-Autostart-enabled=true\n' >> ~/.config/autostart/open-karaoke.desktop
```

- 자동 시작을 끄려면 위 파일을 삭제하면 됩니다.
- 실행 후 **F 키**로 전체 화면 전환, **ESC 키**로 전체 화면 해제입니다(시작 인자로 전체 화면을 켜는 옵션은 없습니다).
- 매장 PC라면 화면 꺼짐/잠금을 꺼 두세요 (GNOME 예):

```bash
gsettings set org.gnome.desktop.session idle-delay 0
gsettings set org.gnome.desktop.screensaver lock-enabled false
```

- 화면이 작은 기기에서 설정 창이 화면을 넘칠 때는 창이 자동으로 화면 높이에 맞춰 조절되며, 내용은 스크롤됩니다.

## 10. 업데이트

설정과 곡 데이터는 실행 폴더 안에 있으므로 **덮어쓰지 말고 새 폴더에 받은 뒤 옮기는** 방식이 안전합니다.

```bash
# 1) 새 배포본을 다른 이름으로 풀기 (예: ~/open-karaoke-new)
# 2) 기존 설정과 데이터를 옮기기
cp ~/open-karaoke/appsettings.local.json ~/open-karaoke-new/ 2>/dev/null
cp -r ~/open-karaoke/data ~/open-karaoke-new/
# 3) 교체
mv ~/open-karaoke ~/open-karaoke-old
mv ~/open-karaoke-new ~/open-karaoke
chmod +x ~/open-karaoke/OpenKaraoke ~/open-karaoke/install-desktop-entry.sh
# 4) yt-dlp 를 앱 폴더에 두었다면 그것도 옮기기
cp ~/open-karaoke-old/yt-dlp ~/open-karaoke/ 2>/dev/null && chmod +x ~/open-karaoke/yt-dlp
# 5) 확인 후 이전 폴더 삭제
rm -rf ~/open-karaoke-old
```

런처를 등록해 두었다면 설치 경로(`~/open-karaoke`)가 같으므로 그대로 사용할 수 있습니다.

## 11. 제거

```bash
rm -f "${XDG_DATA_HOME:-$HOME/.local/share}/applications/open-karaoke.desktop" \
      ~/.config/autostart/open-karaoke.desktop
rm -rf ~/open-karaoke
```

내려받은 곡과 설정이 모두 삭제됩니다. 보관하려면 `data/`, `appsettings.local.json` 을 먼저 백업하세요.

## 12. 알려진 제한 (리눅스)

- **x86_64 전용** — ARM 보드(라즈베리파이 등, aarch64)는 지원하지 않습니다.
- **오디오 장치 선택 UI 없음** — 항상 시스템 기본 출력 장치로 재생합니다.
- **Wayland는 XWayland 경유로 동작**합니다(`xwayland` 패키지 필요). 문제가 있으면 로그인 화면에서
  "Xorg 세션"으로 로그인해 보세요.
- 라즈베리파이용 배포본이나 32비트(x86) 배포본은 제공하지 않습니다.
- 한글 입력기(IBus/Fcitx5) 조합 입력은 데스크톱 환경에 따라 제약이 있을 수 있습니다. 입력이 되지 않으면
  곡명을 붙여넣기(Ctrl+V)로 넣거나, 곡 추가는 Windows PC에서 미리 해 두는 방법을 사용하세요.
  (이 항목은 리눅스 PC에서의 실제 입력기 환경 검증이 필요합니다.)
- **앱으로 설치한 ffmpeg 는 재시작 후 적용** — 재생 엔진이 시작 시 ffmpeg 경로를 고정하기 때문입니다.
  yt-dlp 는 재시작 없이 즉시 사용됩니다.
- **자동 설치를 쓰려면 설치 폴더에 쓰기 권한**이 있어야 합니다(기본 `~/open-karaoke` 는 문제 없음).
  `/opt` 등 읽기 전용 위치에 두었다면 자동 설치가 실패하므로 3장의 `sudo apt install` 방법을 사용하세요.
- 자동 설치에는 인터넷 연결과 `tar`/`xz-utils`(Ubuntu/Debian 기본 포함)가 필요합니다.
- **가사 영상은 소프트웨어 디코딩**입니다(ffmpeg). 1080p 30fps 1곡 재생 시 CPU 코어 1개 정도를 사용합니다.
  GPU 가속이나 하드웨어 디코딩은 쓰지 않으므로, 저사양 PC(2코어 이하)에서는 영상이 끊길 수 있습니다.
  이때는 영상 표시를 포기하고 **내 라이브러리** 탭에서 소리만 들으며 진행하거나,
  더 낮은 화질의 영상으로 다시 받아 저장하세요(곡당 용량도 줄어듭니다).
- **창 위치를 앱이 강제로 지정하는 기능은 X11 세션에서만 확실히 동작합니다.** Wayland 세션에서는
  합성기 정책상 창 위치가 무시될 수 있어, 고객용 TV 창이 다른 모니터에 뜰 수 있습니다. 이 경우
  **창을 마우스로 끌어** 원하는 모니터로 옮기면 됩니다(테두리가 없는 창이라 끌기로 이동합니다).
  확실한 동작이 필요하면 Xorg 세션으로 로그인하세요.
- 가사 영상은 재생 중에만 만들어집니다. 보조 모니터가 없을 때 **노래방 화면** 탭을 벗어나면 영상 처리가
  중단되어 소리만 나옵니다(사양이 낮은 PC를 위한 의도된 동작입니다). 보조 모니터를 쓰는 경우에는
  다른 탭에서 곡을 고르는 동안에도 고객용 화면에 가사가 계속 표시됩니다.

## 13. 문제 해결

**먼저 로그를 확인하세요.** 실행 파일 옆 `logs/app.log` 마지막 부분에 원인이 남습니다.

```bash
tail -n 50 ~/open-karaoke/logs/app.log
```

| 증상 | 원인과 해결 |
| --- | --- |
| `Permission denied` 로 실행이 안 됨 | `chmod +x OpenKaraoke install-desktop-entry.sh` |
| `No such file or directory` 인데 파일은 있음 | 잘못된 아키텍처 또는 실행 권한 없음 → `file OpenKaraoke` 로 ELF x86-64 확인, `aarch64` 면 미지원 |
| 창이 뜨지 않음 / `Unable to open X display` | `libx11-6 libice6 libsm6 libfontconfig1 libgl1` 설치, Wayland면 `xwayland` 설치, Xorg 세션으로 로그인 |
| 글자가 네모(□□)로 보임 | `sudo apt install fonts-noto-cjk` |
| `ffmpeg를 찾을 수 없습니다` | ⚙ 설정 → **누락 도구 자동 설치**, 또는 `sudo apt install ffmpeg` 후 재실행, 또는 ⚙ 설정에서 경로 지정 |
| `yt-dlp가 없습니다` | ⚙ 설정 → **누락 도구 자동 설치**, 또는 4장대로 설치, 또는 ⚙ 설정에서 경로 지정 |
| 검색 결과가 없음 | API 키 미설정·오타, 할당량 초과(하루 한도), 네트워크 차단 → ⚙ 설정에서 확인 |
| 다운로드가 실패함 | yt-dlp 버전이 오래됨 → 방법 A(공식 바이너리)로 교체 후 `./yt-dlp -U` |
| 소리가 나지 않음 | 기본 출력 장치가 TJ 믹서인지 확인(7장), `alsamixer` 음소거 해제, `ALSOFT_LOGLEVEL=3` 로 진단 |
| 소리가 끊기거나 지직거림 | 사용하지 않는 오디오 서비스 종료, 화면·CPU 절전 해제, `ALSOFT_DRIVERS` 로 다른 백엔드 시도 |
| 설정을 저장했는데 적용되지 않음 | 같은 이름의 환경 변수가 있으면 그것이 우선 → `echo $OPENKARAOKE_FFMPEG` 등 확인 후 해제 |
| `찾아보기` 가 열리지 않음 | 파일 선택 포털 부재 → 경로를 직접 입력 |
| 재부팅 후 곡 목록이 사라짐 | 설치 폴더에 쓰기 권한이 없음(`/opt` 등) → `~/open-karaoke` 로 이동 |
| 노래방 화면이 검은색만 나옴 | 예전에 받은 오디오 전용 곡 → 목록에서 **영상 다시 받기**. ffmpeg 미설치여도 같은 증상이 나오므로 ⚙ 설정에서 경로 확인 (`logs/app.log` 의 `[video] 영상 스트림 없음` 줄 참고) |
| 고객용 TV에 창이 안 뜸 | TV가 **확장 모드**로 연결되어 있는지 확인(복제 모드에서는 감지되지 않을 수 있음). Wayland 세션이면 창이 다른 모니터에 떴을 수 있으니 마우스로 끌어 옮기세요 |
| 영상이 끊기거나 화면이 늦게 따라옴 | `top` 으로 CPU 확인 → 다른 프로그램 종료, 저사양이면 더 낮은 화질로 다시 받기. 창 위치 이동은 마우스 끌기로 |
| 영상은 나오는데 소리와 따로 놀음 | 소리가 기본 출력 장치(TJ 믹서)로 나가는지 확인(7장). 영상은 소리 위치를 기준으로 맞춰지므로, 블루투스 등 지연이 큰 장치를 쓰면 어긋날 수 있습니다 |
| 앱이 갑자기 종료됨 | `logs/app.log` 의 `[FATAL]` 줄 확인 (예외 메시지가 기록됩니다) |

## 14. 참고

- 요약 설치 절차·설정·데이터 위치: [README.md](README.md)
- 라이선스/서드파티 고지: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
- YouTube 영상 다운로드는 각 영상의 저작권과 YouTube 서비스 약관을 확인한 뒤 사용하세요.
