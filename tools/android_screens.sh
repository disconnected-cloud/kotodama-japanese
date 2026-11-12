#!/usr/bin/env bash
# Проверка Android-версии в эмуляторе (вызывается из .github/workflows/android.yml).
# Ставит отладочный APK, запускает приложение и снимает экраны: каждый экран открывается
# флагом отладочной сборки (intent extra «args», как --page/--open на компьютере).
# Падает, если в журнале есть FATAL EXCEPTION.
set -u
PKG=io.github.disconnectedcloud.kotodama
adb install -r "$(ls out/x64/*-Signed.apk | head -1)"
adb shell pm grant $PKG android.permission.RECORD_AUDIO || true
ACT=$PKG/$(adb shell cmd package resolve-activity --brief $PKG | tail -1 | tr -d '\r' | cut -d/ -f2)
echo "activity: $ACT"
adb shell am start -n "$ACT"
sleep 25
adb exec-out screencap -p > android-start.png
for s in "--page home" "--page path" "--page kana" "--page kanji" "--page words" "--page grammar" "--page speech" \
         "--page review" "--page more" "--page dict" "--page trainers" "--page stats" \
         "--open quiz" "--open learnq" "--open dialog" "--open wod" "--open week" "--open setvoice"; do
  name=$(echo "$s" | tr -d '-' | tr ' ' '_')
  adb shell am start -S -n "$ACT" --es args "'$s'" > /dev/null
  sleep 14
  adb exec-out screencap -p > "android-$name.png"
  echo "снимок: $s"
done
adb logcat -d | grep -iE "kotodama|mono|dotnet|avalonia|FATAL|AndroidRuntime" | tail -300 > android-log.txt || true
if grep -q "FATAL EXCEPTION" android-log.txt; then
  echo "::error::приложение упало в эмуляторе"
  grep -A20 "FATAL EXCEPTION" android-log.txt | head -40
  exit 1
fi
