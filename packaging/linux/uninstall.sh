#!/bin/sh
# Удаление Kotodama. Прогресс (папка ~/.config/Matsubakira) остаётся — удали её вручную, если нужно.
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
rm -rf "$DATA/kotodama" "$DATA/applications/kotodama.desktop" "$DATA/icons/hicolor/256x256/apps/kotodama.png"
echo "Kotodama удалена. Прогресс сохранён в ~/.config/Matsubakira"
