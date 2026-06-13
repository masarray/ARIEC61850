@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0verify-source-clean.ps1" %*
exit /b %ERRORLEVEL%
