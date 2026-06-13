@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-windows-singlefile.ps1" %*
exit /b %ERRORLEVEL%
