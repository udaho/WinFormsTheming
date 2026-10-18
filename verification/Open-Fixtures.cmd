@echo off
setlocal
dotnet build "%~dp0Fixtures\HenLib.WinFormsTheming.Fixtures.csproj" -c Debug --nologo
if errorlevel 1 exit /b 1
set "fixture_exe=%~dp0Fixtures\bin\Debug\net10.0-windows\HenLib.WinFormsTheming.Fixtures.exe"
start "Light fixture" "%fixture_exe%" --light %*
start "Dark fixture" "%fixture_exe%" --dark %*
