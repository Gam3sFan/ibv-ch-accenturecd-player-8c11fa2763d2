@echo off
setlocal

rem Usage:
rem   build-update-package.cmd                         -> auto-increment patch (1.0.0 -> 1.0.1), rebuild, package
rem   build-update-package.cmd 1.5.0                    -> force an explicit version
rem   build-update-package.cmd "" D:\update-server      -> auto-increment, write ZIP+manifest elsewhere
rem   build-update-package.cmd "" "" http://host:8080   -> auto-increment, custom server base URL
rem
rem Args (all optional): %1 = version  %2 = output dir  %3 = base URL

set "VERSION=%~1"
set "OUTPUT_DIR=%~2"
set "BASE_URL=%~3"

set "PS_ARGS="
if not "%VERSION%"==""    set "PS_ARGS=%PS_ARGS% -Version "%VERSION%""
if not "%OUTPUT_DIR%"=="" set "PS_ARGS=%PS_ARGS% -OutputDir "%OUTPUT_DIR%""
if not "%BASE_URL%"==""   set "PS_ARGS=%PS_ARGS% -BaseUrl "%BASE_URL%""

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-update-package.ps1"%PS_ARGS%
set "EXITCODE=%ERRORLEVEL%"

rem Keep the window open only when launched by double-click.
echo %cmdcmdline% | find /i "%~nx0" >nul
if not errorlevel 1 pause

exit /b %EXITCODE%
