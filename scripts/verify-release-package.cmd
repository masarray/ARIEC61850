@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0verify-release-package.ps1" %*
exit /b %ERRORLEVEL%
