@echo off
setlocal
title PrismCast Build
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-local.ps1"
set EXITCODE=%ERRORLEVEL%
echo.
if not "%EXITCODE%"=="0" (
  echo BUILD FAILED with exit code %EXITCODE%.
) else (
  echo BUILD FINISHED SUCCESSFULLY.
)
echo.
pause
exit /b %EXITCODE%
