#!/usr/bin/env sh
# Builds the self-contained linux-x64 bundle directly on a Linux machine
# (use scripts/publish.ps1 -Runtime linux-x64 when building on Windows).
set -eu

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
out=${1:-"$repo_root/artifacts/linux-x64"}
project="$repo_root/src/OpenKaraoke.Desktop/OpenKaraoke.Desktop.csproj"

case "$out" in
    /|"$HOME"|"$repo_root")
        echo "Refusing to use an unsafe output path: $out" >&2
        exit 1
        ;;
esac

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet SDK를 찾을 수 없습니다. .NET 8 SDK를 설치하세요: https://dotnet.microsoft.com/download/dotnet/8.0" >&2
    exit 1
fi

rm -rf "$out"

dotnet publish "$project" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    -o "$out"

cp "$repo_root/THIRD-PARTY-NOTICES.md" "$repo_root/README.md" "$out/"
cp "$repo_root/packaging/open-karaoke.desktop.in" "$repo_root/packaging/install-desktop-entry.sh" "$out/"
chmod +x "$out/OpenKaraoke" "$out/install-desktop-entry.sh"

echo ""
echo "배포 폴더: $out"
echo "실행: \"$out/OpenKaraoke\""
echo "앱 메뉴 등록(선택): \"$out/install-desktop-entry.sh\""
echo "ffmpeg가 필요합니다(예: sudo apt install ffmpeg fonts-noto-cjk)"
