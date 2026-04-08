@echo off
taskkill /IM Veilr.exe /F >nul 2>&1
timeout /t 2 /nobreak >nul
dotnet publish src/Veilr -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o .\dist
if %ERRORLEVEL% NEQ 0 (echo Build failed && exit /b 1)
echo Build complete: dist\Veilr.exe
