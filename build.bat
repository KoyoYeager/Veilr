@echo off
echo Veilr ビルド中...
dotnet publish src/Veilr -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o .\dist
if %ERRORLEVEL% NEQ 0 (echo ビルド失敗 && exit /b 1)
echo.
echo ビルド完了: dist\Veilr.exe
