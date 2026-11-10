@echo off
setlocal
cd /d "%~dp0"
set "UNO_EXE=%~dp0src\StarfleetLibrary.Uno\bin\Release\net10.0-windows10.0.26100\win-x64\StarfleetLibrary.Uno.exe"
if exist "%UNO_EXE%" goto launch
dotnet build src\StarfleetLibrary.Uno\StarfleetLibrary.Uno.csproj -c Release -f net10.0-windows10.0.26100
if errorlevel 1 goto failed
:launch
start "" "%UNO_EXE%"
exit /b 0
:failed
echo The Uno app could not be built. See the error above.
pause
exit /b 1
