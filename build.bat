@echo off
REM Kill any process locking dist\Veilr.exe
taskkill /IM Veilr.exe /F >nul 2>&1
for /f "tokens=2" %%p in ('tasklist /fi "imagename eq dotnet.exe" /fo csv /nh 2^>nul ^| findstr /i "dotnet"') do (
    taskkill /PID %%~p /F >nul 2>&1
)
if exist dist\Veilr.exe (
    del /f dist\Veilr.exe >nul 2>&1
    if exist dist\Veilr.exe (
        echo ERROR: dist\Veilr.exe is locked. Close the app first.
        exit /b 1
    )
)
dotnet publish src/Veilr -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o .\dist
if %ERRORLEVEL% NEQ 0 (echo Build failed && exit /b 1)
echo Build complete: dist\Veilr.exe
