@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0clean-cache.ps1" %*
if errorlevel 1 (
  echo.
  echo 清理失败，请查看上方错误信息。
  pause
)
