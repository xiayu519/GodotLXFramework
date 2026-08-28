@echo off
setlocal

set "LX_DESIGN_DIR=%~dp0"
set "LX_NO_PAUSE="
if /I "%~1"=="--no-pause" set "LX_NO_PAUSE=1"

pushd "%LX_DESIGN_DIR%" >nul
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%LX_DESIGN_DIR%build.ps1"
set "LX_BUILD_EXIT=%ERRORLEVEL%"
popd >nul

if "%LX_BUILD_EXIT%"=="0" (
    echo.
    echo [LXFramework] Luban tables installed successfully.
) else (
    echo.
    echo [LXFramework] Luban table build failed with exit code %LX_BUILD_EXIT%.
)

if not defined LX_NO_PAUSE pause
exit /b %LX_BUILD_EXIT%
