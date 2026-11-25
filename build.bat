@echo off
setlocal
cd /d "%~dp0"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "CSC=%FW%\csc.exe"
set "WM=%WINDIR%\System32\WinMetadata"
if not exist "%CSC%" (
  echo Could not find the C# compiler that comes with Windows ^(.NET Framework 4^).
  if not "%1"=="nopause" pause
  exit /b 1
)
rem Quit a running bar cleanly (this also brings the Windows taskbar back) so the exe is not locked.
if exist "%~dp0StayBar.exe" (
  "%~dp0StayBar.exe" --quit >nul 2>&1
  for /l %%i in (1,1,20) do (
    tasklist /fi "imagename eq StayBar.exe" 2>nul | find /i "StayBar.exe" >nul && ping -n 2 127.0.0.1 >nul
  )
)
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:"%~dp0StayBar.exe" /win32manifest:app.manifest /codepage:65001 ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Data.dll /r:System.Numerics.dll /r:"%FW%\System.Numerics.Vectors.dll" ^
  /r:"%FW%\System.Runtime.dll" /r:"%FW%\System.Runtime.InteropServices.WindowsRuntime.dll" /r:"%FW%\System.Runtime.WindowsRuntime.dll" ^
  /r:"%WM%\Windows.Foundation.winmd" /r:"%WM%\Windows.Media.winmd" /r:"%WM%\Windows.Devices.winmd" ^
  /r:"%WM%\Windows.Networking.winmd" /r:"%WM%\Windows.Storage.winmd" /r:"%WM%\Windows.UI.winmd" ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo Build FAILED - copy the errors above and send them to Claude.
  if not "%1"=="nopause" pause
  exit /b 1
)
echo.
echo Built StayBar.exe - double-click it to start the bar.
if not "%1"=="nopause" pause
