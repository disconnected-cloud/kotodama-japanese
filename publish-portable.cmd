@echo off
rem Портативная сборка: .NET встроен, данные хранятся в папке data рядом с exe.
rem Работает на чистой Windows 10/11 x64 без установки чего-либо.
dotnet publish Matsubakira.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:PublishReadyToRun=true -o dist-portable
type nul > dist-portable\portable
echo.
echo Готово: папка dist-portable. Переносить её целиком.
