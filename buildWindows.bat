:<<"::BATCH"
@rem Polyglot script: runs on Windows (buildWindows.bat) and Linux/macOS (bash buildWindows.bat)
@echo off
if exist Build\win-x64 rmdir /s /q Build\win-x64
mkdir Build

echo Building for Github Windows x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Release -r win-x64 -o ./Build/win-x64

echo Done!
goto :eof
::BATCH
# Linux/macOS -- run with: bash buildWindows.bat
rm -rf Build/win-x64
mkdir -p Build

echo "Building for Github Windows x64..."
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Release -r win-x64 -o ./Build/win-x64

echo "Done!"
