@echo off
call release.bat
if %ERRORLEVEL% NEQ 0 exit /b 1
powershell -Command "Compress-Archive -Path '.\dist\Veilr.exe' -DestinationPath '.\dist\Veilr-win-x64.zip' -Force"
echo パッケージ作成完了: dist\Veilr-win-x64.zip
