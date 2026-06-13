@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0clean-local-artifacts.ps1" %*
exit /b %ERRORLEVEL%
