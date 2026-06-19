:<<"::BATCH"
@rem Polyglot script: runs on Windows (buildLinux.bat) and Linux/macOS (bash buildLinux.bat)
@echo off
if exist Build\linux-x64 rmdir /s /q Build\linux-x64
mkdir Build

echo Building for Github Linux x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Release -r linux-x64 -o ./Build/linux-x64

echo Done!
goto :eof
::BATCH
# Linux/macOS -- run with: bash buildLinux.bat
rm -rf Build/linux-x64
mkdir -p Build

echo "Building for Github Linux x64..."
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Release -r linux-x64 -o ./Build/linux-x64
chmod +x ./Build/linux-x64/VRCVideoCacher

echo "Done!"
