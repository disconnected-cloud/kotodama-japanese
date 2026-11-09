#!/bin/sh
# Установка Kotodama для текущего пользователя (права root не нужны):
# программа → ~/.local/share/kotodama, ярлык → меню приложений.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
DEST="${XDG_DATA_HOME:-$HOME/.local/share}/kotodama"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICONS="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"

if [ "$HERE" != "$DEST" ]; then
  mkdir -p "$DEST"
  cp -rf "$HERE"/. "$DEST"/
fi
chmod +x "$DEST/Kotodama" "$DEST/install.sh" "$DEST/uninstall.sh" 2>/dev/null || true
mkdir -p "$APPS" "$ICONS"
cp -f "$DEST/kotodama.png" "$ICONS/kotodama.png"
sed "s|@EXEC@|$DEST/Kotodama|g; s|@ICON@|kotodama|g" "$DEST/kotodama.desktop" > "$APPS/kotodama.desktop"
chmod +x "$APPS/kotodama.desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS" >/dev/null 2>&1 || true
echo "Kotodama установлена: $DEST"
echo "Запуск — из меню приложений или командой: $DEST/Kotodama"

# Подсказки по зависимостям (запись и звук)
if ! command -v parecord >/dev/null 2>&1 && ! command -v pw-record >/dev/null 2>&1 && ! command -v arecord >/dev/null 2>&1; then
  echo "Для проверки произношения нужна программа записи звука: установи pulseaudio-utils, pipewire или alsa-utils."
fi
