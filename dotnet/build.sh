#!/usr/bin/env bash
# Builds the self-contained Windows (x64) exe into publish/TapFunSample/ and zips it as publish/TapFunSample.zip.
# Runs on Windows (Git Bash), macOS and Linux with the .NET 10 SDK.
set -euo pipefail
cd "$(dirname "$0")"
rm -rf publish
dotnet publish TapFun.Sample -c Release -o publish/TapFunSample
rm -f publish/TapFunSample/*.pdb
# The zip has one top folder, so the exe to launch is TapFunSample\TapFunSample.exe.
(cd publish && zip -qr TapFunSample.zip TapFunSample)
echo "exe: $(pwd)/publish/TapFunSample/TapFunSample.exe"
echo "zip: $(pwd)/publish/TapFunSample.zip"
