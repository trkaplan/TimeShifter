@echo off
chcp 65001 >nul
REM TimeShifter Uninstaller Script (Batch)
REM Bu script PowerShell uninstaller'ı çalıştırır

echo ========================================
echo   TimeShifter Uninstaller
echo ========================================
echo.

REM PowerShell script'inin yolunu bul
set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%uninstall-script"

REM PowerShell script'i var mı kontrol et
if not exist "%PS_SCRIPT%" (
    echo HATA: uninstall-script dosyasi bulunamadi!
    echo.
    pause
    exit /b 1
)

REM PowerShell ile script'i çalıştır (UTF-8 encoding ile)
echo PowerShell uninstaller baslatiliyor...
echo.

powershell.exe -ExecutionPolicy Bypass -Command "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; [Console]::InputEncoding = [System.Text.Encoding]::UTF8; & '%PS_SCRIPT%'"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo HATA: Uninstaller calistirilirken bir hata olustu.
    echo.
    pause
    exit /b 1
)

exit /b 0

