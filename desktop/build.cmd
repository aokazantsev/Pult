@echo off
setlocal
cd /d "%~dp0"
set APP=Pult
set HOOK=PultHook
set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set REFS=/r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll

call :free %APP%
call :free %HOOK%
%CSC% /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /out:%APP%.exe /win32manifest:src\app.manifest /win32icon:src\app.ico %REFS% /recurse:src\*.cs
if errorlevel 1 exit /b 1
copy /y src\app.config %APP%.exe.config >nul
%CSC% /nologo /codepage:65001 /target:exe /platform:x64 /optimize+ /out:%HOOK%.exe /r:System.Web.Extensions.dll hook\*.cs
if errorlevel 1 exit /b 1
copy /y src\app.config %HOOK%.exe.config >nul
if /i not "%1"=="nostart" start "" "%~dp0%APP%.exe"
exit /b 0

:free
if exist %1.exe del /f /q %1.exe >nul 2>&1
if exist %1.exe (
    if exist %1.old.exe del /f /q %1.old.exe >nul 2>&1
    ren %1.exe %1.old.exe
)
exit /b 0
