@echo off
setlocal
cd /d "%~dp0.."
set PLATFORM=%1
if "%PLATFORM%"=="" set PLATFORM=x64
if /i "%PLATFORM%"=="x86" (set CSC="%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe") else (set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe")
set OUT=%~dp0obj\%PLATFORM%
if not exist "%OUT%" mkdir "%OUT%"
%CSC% /nologo /codepage:65001 /target:exe /platform:%PLATFORM% /main:Pult.Tests.Tests /out:"%OUT%\Tests.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Numerics.dll /recurse:src\*.cs tests\*.cs
if errorlevel 1 exit /b 1
"%OUT%\Tests.exe"
