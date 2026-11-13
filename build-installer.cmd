@echo off
chcp 65001 >nul
cd /d "%~dp0"
rem Сборка установщика Kotodama. Нужен Inno Setup 6: winget install JRSoftware.InnoSetup
rem Перед этим запусти publish-portable.cmd или используй готовую папку release\Kotodama-portable.
set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" (echo Inno Setup 6 не найден. Установи: winget install JRSoftware.InnoSetup & pause & exit /b 1)
"%ISCC%" installer\Kotodama.iss
if errorlevel 1 (echo. & echo Ошибка сборки, см. сообщения выше. & pause & exit /b 1)
echo.
echo Готово: release\Kotodama-Setup-2.5.0.exe
pause
