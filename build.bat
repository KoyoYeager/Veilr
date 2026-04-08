@echo off
dotnet publish src/Veilr -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o .\dist
if %ERRORLEVEL% NEQ 0 (echo Build failed && exit /b 1)
echo Build complete: dist\Veilr.exe
