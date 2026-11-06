@echo off
setlocal
cd /d "%~dp0.."
set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set OUT=%~dp0obj
if not exist "%OUT%" mkdir "%OUT%"
%CSC% /nologo /codepage:65001 /target:exe /platform:x64 /main:Pult.Tests.Tests /out:"%OUT%\Tests.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /recurse:src\*.cs tests\*.cs
if errorlevel 1 exit /b 1
"%OUT%\Tests.exe"
