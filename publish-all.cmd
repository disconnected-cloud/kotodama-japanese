@echo off
chcp 65001 >nul
cd /d "%~dp0"
rem Сборка всех файлов релиза из кроссплатформенной версии (xplat): Windows (обычная, портативная), Linux, установщик.
set OUT=release
if exist %OUT%\Kotodama rmdir /s /q %OUT%\Kotodama
if exist %OUT%\Kotodama-portable rmdir /s /q %OUT%\Kotodama-portable
if exist %OUT%\Kotodama-linux rmdir /s /q %OUT%\Kotodama-linux
del /q %OUT%\*.zip %OUT%\*.tar.gz %OUT%\*.exe 2>nul
rem 4.2: и обычный архив со встроенным .NET 10 — у кого стоял только .NET 8, автообновление не сломает запуск
dotnet publish xplat -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o %OUT%\Kotodama || goto :err
dotnet publish xplat -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o %OUT%\Kotodama-portable || goto :err
type nul > %OUT%\Kotodama-portable\portable
copy /y "packaging\windows\ПРОЧТИ.txt" %OUT%\Kotodama-portable\ >nul
dotnet publish xplat -c Release -r linux-x64 --self-contained true -o %OUT%\Kotodama-linux || goto :err
powershell -NoProfile -Command "Compress-Archive -Force '%OUT%\Kotodama\*' '%OUT%\Kotodama-win-x64.zip'; Compress-Archive -Force '%OUT%\Kotodama-portable\*' '%OUT%\Kotodama-portable-win-x64.zip'"
python tools\pack_linux.py %OUT%\Kotodama-linux %OUT%\Kotodama-linux-x64.tar.gz
call build-installer.cmd
rem Контрольные суммы для автообновления (формат sha256sum): программа сверяет архив перед установкой
powershell -NoProfile -Command "Set-Location '%OUT%'; Get-ChildItem *.zip,*.tar.gz,*.exe | ForEach-Object { (Get-FileHash $_.Name -Algorithm SHA256).Hash.ToLower() + '  ' + $_.Name } | Set-Content -Encoding ascii SHA256SUMS"
echo.
echo Готово: файлы релиза в папке %OUT%
pause
exit /b 0
:err
echo Ошибка сборки
pause
exit /b 1
