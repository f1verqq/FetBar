@echo off
rem Closes StayBar and brings the normal Windows taskbar back.
if exist "%~dp0StayBar.exe" (
  "%~dp0StayBar.exe" --restore
) else (
  taskkill /f /im StayBar.exe >nul 2>&1
  powershell -NoProfile -Command "$s=(Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3).Settings; $s[8]=2; Set-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3 -Name Settings -Value $s; Stop-Process -Name explorer -Force"
)
