:<<"::BATCH"
@echo off
if exist Build rmdir /s /q Build
if exist VRCVideoCacher\bin rmdir /s /q VRCVideoCacher\bin
if exist VRCVideoCacher\obj rmdir /s /q VRCVideoCacher\obj
mkdir Build

echo Building for Github Windows x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Release -r win-x64 -o ./Build/win-x64

echo Building for Github Linux x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Release -r linux-x64 -o ./Build/linux-x64

echo Building for Steam Windows x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c SteamRelease -r win-x64 -o ./Build/Steam/win-x64

echo Building for Steam Linux x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c SteamRelease -r linux-x64 -o ./Build/Steam/linux-x64

echo Done!
goto :eof
::BATCH
# Linux/macOS -- run with: bash build.bat
rm -rf Build VRCVideoCacher/bin VRCVideoCacher/obj
mkdir -p Build

echo "Building for Github Windows x64..."
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Release -r win-x64 -o ./Build/win-x64

echo "Building for Github Linux x64..."
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Release -r linux-x64 -o ./Build/linux-x64
chmod +x ./Build/linux-x64/VRCVideoCacher

echo "Building for Steam Windows x64..."
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c SteamRelease -r win-x64 -o ./Build/Steam/win-x64

echo "Building for Steam Linux x64..."
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c SteamRelease -r linux-x64 -o ./Build/Steam/linux-x64
chmod +x ./Build/Steam/linux-x64/VRCVideoCacher

echo "Done!"
