@echo off
dotnet build src/Veilr -c Debug
if %ERRORLEVEL% NEQ 0 (echo ビルド失敗 && exit /b 1)
echo ビルド成功
