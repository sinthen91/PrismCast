@echo off
setlocal EnableExtensions
color 0F
title PrismCast Build
cls

echo ============================================================
echo                     PRISMCAST BUILD
echo ============================================================
echo.
echo Source folder: "%~dp0"
echo.

if not exist "%~dp0build-local.ps1" (
  echo ERROR: build-local.ps1 is missing from this folder.
  echo Extract the complete source ZIP before running Build.cmd.
  goto :failed
)

where powershell.exe >nul 2>nul
if errorlevel 1 (
  echo ERROR: Windows PowerShell could not be found.
  goto :failed
)

echo Starting PowerShell build script...
echo The first build may take several minutes while .NET is downloaded.
echo Do not close this window.
echo.

powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0build-local.ps1"
set "EXITCODE=%ERRORLEVEL%"
echo.
if not "%EXITCODE%"=="0" goto :failed_with_code

echo ============================================================
echo BUILD FINISHED SUCCESSFULLY
echo ============================================================
echo.
pause
exit /b 0

:failed_with_code
echo BUILD FAILED with exit code %EXITCODE%.
echo Diagnostic log: "%~dp0build.log"
goto :failed_pause

:failed
set "EXITCODE=1"

:failed_pause
echo.
echo ============================================================
echo BUILD FAILED
echo ============================================================
echo.
pause
exit /b %EXITCODE%
