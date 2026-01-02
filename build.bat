@echo off
:: Bat dosyasinin bulundugu klasore git
cd /d "%~dp0"

echo TimeShifter derleniyor...
echo Klasor: %cd%
echo.

:: Kaynak dosya var mi kontrol et
if not exist "TimeShifter.cs" (
    echo HATA: TimeShifter.cs bulunamadi!
    echo Bu dosyanin build.bat ile ayni klasorde olmasi gerekiyor.
    pause
    exit /b 1
)

:: .NET Framework CSC yolunu bul
set CSC_PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC_PATH%" (
    set CSC_PATH=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
)

if not exist "%CSC_PATH%" (
    echo HATA: CSC bulunamadi!
    echo .NET Framework 4.0+ yuklu olmali.
    pause
    exit /b 1
)

:: Derle
"%CSC_PATH%" /target:winexe /win32icon:icon.ico /out:TimeShifter.exe TimeShifter.cs

if exist TimeShifter.exe (
    echo.
    echo ========================================
    echo Basarili! TimeShifter.exe olusturuldu.
    echo ========================================
    echo.
    echo Calistirmak icin TimeShifter.exe dosyasina cift tiklayin.
) else (
    echo HATA: Derleme basarisiz!
)
pause
