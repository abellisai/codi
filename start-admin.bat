@echo off
setlocal
pushd "%~dp0src\Qwerty.Codi.Web\projects\admin" || exit /b 1

where node >nul 2>nul
if errorlevel 1 goto missingRuntime
where npm.cmd >nul 2>nul
if errorlevel 1 goto missingRuntime
if not exist "..\..\node_modules\@angular\cli\bin\ng.js" goto missingPackages

echo Avvio admin Codi: https://localhost:4200/admin/
call npm.cmd run start
set "exitCode=%errorlevel%"
popd
if not "%exitCode%"=="0" (
    echo Avvio admin terminato con codice %exitCode%.
    pause
)
exit /b %exitCode%

:missingRuntime
echo Node o npm non trovati. Attiva la versione del progetto con nvm e riapri il terminale.
goto failure
:missingPackages
echo Dipendenze Angular mancanti. Esegui npm ci nella cartella src\Qwerty.Codi.Web.
:failure
popd
pause
exit /b 1
