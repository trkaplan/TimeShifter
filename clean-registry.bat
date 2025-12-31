@echo off
REM TimeShifter Registry Temizleyici
REM Sadece registry kayıtlarını siler

echo TimeShifter registry kayitlari temizleniyor...
echo.

REM PowerShell ile registry temizleme
powershell.exe -Command "Remove-Item -Path 'HKCU:\Software\TimeShifter' -Recurse -Force -ErrorAction SilentlyContinue; $notifyPath = 'HKCU:\Control Panel\NotifyIconSettings'; if (Test-Path $notifyPath) { Get-ChildItem -Path $notifyPath | ForEach-Object { $exePath = Get-ItemProperty -Path $_.PSPath -Name 'ExecutablePath' -ErrorAction SilentlyContinue; if ($exePath -and $exePath.ExecutablePath -like '*TimeShifter*') { Remove-Item -Path $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue } } }; Write-Host 'Registry temizlendi.'"

echo.
echo Tamamlandi.
pause

