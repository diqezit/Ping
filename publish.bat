@echo off
dotnet publish PingTester.csproj -c Release -r win-x64   -o out\PingTester-v2.5-win
dotnet publish PingTester.csproj -c Release -r linux-x64 -o out\PingTester-v2.5-linux
dotnet publish PingTester.csproj -c Release -r osx-x64   -o out\PingTester-v2.5-mac
echo Done
pause