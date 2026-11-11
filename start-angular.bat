@echo off
setlocal

rem Ogni app rimane in un terminale distinto, con arresto indipendente tramite Ctrl+C.
start "Codi - Admin" /D "%~dp0" "%ComSpec%" /d /c ""%~dp0start-admin.bat""
if errorlevel 1 goto failure
start "Codi - Public" /D "%~dp0" "%ComSpec%" /d /c ""%~dp0start-public.bat""
if errorlevel 1 goto failure
exit /b 0

:failure
echo Impossibile aprire uno dei terminali Angular. Usa i file start-admin.bat e start-public.bat.
pause
exit /b 1
