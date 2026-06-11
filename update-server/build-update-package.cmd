@echo off
setlocal

set VERSION=%~1
set OUTPUT_DIR=%~2
if "%VERSION%"=="" (
  set /p VERSION=Version to publish, e.g. 1.0.1: 
)

if "%VERSION%"=="" (
  echo Version is required.
  exit /b 1
)

if "%OUTPUT_DIR%"=="" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-update-package.ps1" -Version "%VERSION%"
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-update-package.ps1" -Version "%VERSION%" -OutputDir "%OUTPUT_DIR%"
)
