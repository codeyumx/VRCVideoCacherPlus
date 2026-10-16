#!/bin/sh
# Run from yt-dlp-stub/. Native AOT cannot cross-compile to Windows from Linux, so on a Linux
# host the project falls back to a trimmed single-file build; --self-contained keeps that build
# from needing an installed .NET runtime.
set -e
dotnet publish -r win-x64 -c Release --self-contained true -o bin/publish/win-x64
cp bin/publish/win-x64/yt-dlp-stub.exe ../VRCVideoCacher/
