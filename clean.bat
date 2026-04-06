@echo off
dotnet clean src/Veilr
if exist build rmdir /s /q build
if exist dist rmdir /s /q dist
echo クリーン完了
