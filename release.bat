@echo off
dotnet publish src/Veilr -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o .\dist
echo リリースビルド完了: dist\Veilr.exe
