@echo off
:: TimeShifter Release Zip Olusturucu
:: Bu script TimeShifter.exe'yi zip dosyasina paketler

cd /d "%~dp0"

echo ========================================
echo TimeShifter Release Zip Olusturuluyor...
echo ========================================
echo.

:: TimeShifter.exe var mi kontrol et
if not exist "TimeShifter.exe" (
    echo HATA: TimeShifter.exe bulunamadi!
    echo Once build.bat ile derleme yapin.
    pause
    exit /b 1
)

:: Versiyon numarasi (opsiyonel - manuel guncellenebilir)
set VERSION=%date:~-4,4%%date:~-7,2%%date:~-10,2%
if "%1" neq "" set VERSION=%1

:: Release klasoru olustur
if not exist "release" mkdir release

:: Eski zip dosyasini sil
if exist "release\TimeShifter-%VERSION%.zip" del "release\TimeShifter-%VERSION%.zip"

:: Zip dosyasi olustur (PowerShell kullanarak)
echo Zip dosyasi olusturuluyor...
powershell -Command "Compress-Archive -Path 'TimeShifter.exe' -DestinationPath 'release\TimeShifter-%VERSION%.zip' -Force"

if exist "release\TimeShifter-%VERSION%.zip" (
    echo.
    echo ========================================
    echo Basarili!
    echo ========================================
    echo.
    echo Release dosyasi: release\TimeShifter-%VERSION%.zip
    echo.
    echo GitHub Release'a yuklemek icin:
    echo 1. GitHub Releases sayfasina git
    echo 2. Yeni release olustur veya mevcut release'i duzenle
    echo 3. Bu zip dosyasini ekle
    echo.
) else (
    echo HATA: Zip dosyasi olusturulamadi!
    echo PowerShell'in Compress-Archive komutunu desteklediginden emin olun.
)

pause

