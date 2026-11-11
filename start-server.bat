@echo off
setlocal
pushd "%~dp0" || exit /b 1

where dotnet >nul 2>nul
if errorlevel 1 (
    echo SDK .NET non trovato. Verifica che sia installato e presente nel PATH.
    goto failure
)

echo Avvio API Codi: https://localhost:5200
call dotnet run --project ".\src\Qwerty.Codi.Api" --launch-profile "Qwerty.Codi.Api"
set "exitCode=%errorlevel%"
popd
if not "%exitCode%"=="0" (
    echo Avvio API terminato con codice %exitCode%.
    pause
)
exit /b %exitCode%

:failure
popd
pause
exit /b 1
