@echo off
rem Build CursorBubble.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] csc.exe was not found. .NET Framework 4.x is required.
  pause
  exit /b 1
)

"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:CursorBubble.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll src\CursorBubble.cs
if errorlevel 1 (
  echo [ERROR] Build failed.
  pause
  exit /b 1
)

echo [OK] CursorBubble.exe was created.
pause
