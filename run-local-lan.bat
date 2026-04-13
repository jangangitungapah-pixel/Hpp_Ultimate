@echo off
setlocal

set PORT=8080
set ASPNETCORE_URLS=http://0.0.0.0:%PORT%
set HOSTNAME=%COMPUTERNAME%

cd /d "%~dp0publish\desktop"

if not exist "Hpp_Ultimate.exe" (
    echo File publish belum ada. Jalankan publish-local-exe.bat dulu.
    exit /b 1
)

echo Menjalankan HPP Ultimate pada %ASPNETCORE_URLS%
echo Buka dari HP melalui:
echo http://%HOSTNAME%:%PORT%
echo Jika hostname belum terbaca di HP, fallback ke IP lokal laptop.
echo.

Hpp_Ultimate.exe

endlocal
