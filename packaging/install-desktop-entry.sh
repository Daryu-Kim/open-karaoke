#!/usr/bin/env sh
# Registers the published Linux build with the desktop environment so the app
# appears in the application menu. Run it from inside the published folder.
set -eu

app_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
target_dir="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
entry="$target_dir/open-karaoke.desktop"

mkdir -p "$target_dir"
# Escape sed metacharacters so paths containing '&', '\' or '|' stay intact.
escaped=$(printf '%s' "$app_dir" | sed -e 's/[\\&|]/\\&/g')
sed "s|@APP_DIR@|$escaped|g" "$app_dir/open-karaoke.desktop.in" > "$entry"

# Copies made from Windows git/zip lose the executable bit.
chmod +x "$app_dir/OpenKaraoke" "$app_dir/install-desktop-entry.sh"

update-desktop-database "$target_dir" 2>/dev/null || true

echo "Desktop entry installed: $entry"
echo "You can now start '오픈 노래방' from the application menu."
