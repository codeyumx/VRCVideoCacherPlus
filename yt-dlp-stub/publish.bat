dotnet publish -r win-x64 -c Release --self-contained true -o bin\publish\win-x64
if errorlevel 1 exit /b 1
xcopy bin\publish\win-x64\yt-dlp-stub.exe ..\VRCVideoCacher\ /Y