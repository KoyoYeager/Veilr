@echo off
call build.bat
if %ERRORLEVEL% NEQ 0 exit /b 1
dotnet run --project src/Veilr
