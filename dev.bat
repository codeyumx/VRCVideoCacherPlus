:<<"::BATCH"
@echo off
if exist VRCVideoCacher\bin rmdir /s /q VRCVideoCacher\bin
if exist VRCVideoCacher\obj rmdir /s /q VRCVideoCacher\obj

echo Building Debug for Windows x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Debug -o Build/dev

echo Done! Output: Build\dev\VRCVideoCacher.exe
goto :eof
::BATCH
# Linux/macOS -- run with: bash dev.bat
rm -rf VRCVideoCacher/bin VRCVideoCacher/obj

echo "Building Debug for Linux x64..."
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Debug -o Build/dev
chmod +x Build/dev/VRCVideoCacher

echo "Done! Output: Build/dev/VRCVideoCacher"
