@echo off
setlocal

set PROJECT=Hpp_Ultimate\Hpp_Ultimate\Hpp_Ultimate\Hpp_Ultimate.csproj
set OUTPUT=publish\desktop

echo Publishing HPP Ultimate to %OUTPUT% ...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:UseAppHost=true -o "%OUTPUT%"

if errorlevel 1 (
    echo.
    echo Publish gagal.
    exit /b 1
)

echo.
echo Publish selesai.
echo EXE: %CD%\%OUTPUT%\Hpp_Ultimate.exe
endlocal
