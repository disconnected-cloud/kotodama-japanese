@echo off
rem Сборка оптимизированной версии в папку dist
dotnet publish Matsubakira.csproj -c Release -o dist
echo.
echo Готово: папка dist — Kotodama.exe и runtimes\win-x64 (нужна для распознавания речи, держи рядом с exe)
