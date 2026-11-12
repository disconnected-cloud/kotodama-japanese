@echo off
rem Сборка для своего компьютера (нужен .NET 8 Desktop Runtime) в папку dist
dotnet publish xplat -c Release -r win-x64 --self-contained false -o dist
echo.
echo Готово: папка dist — Kotodama.exe и runtimes (нужны для распознавания речи, держи рядом с exe)
