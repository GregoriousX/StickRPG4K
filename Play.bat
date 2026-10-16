@echo off
setlocal
cd /d "%~dp0"
set "FPS=%~1"
if not defined FPS set "FPS=60"
if not "%FPS%"=="30" if not "%FPS%"=="60" if not "%FPS%"=="120" (
    echo Usage: Play.bat [30^|60^|120] [windowed]
    exit /b 1
)
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
if errorlevel 1 (
    echo Installation failed. Read the message above.
    pause
    exit /b 1
)
set "DISPLAY_ARGS=--fullscreen"
if /i "%~2"=="windowed" set "DISPLAY_ARGS=--width 1600 --height 900"
if not exist "saves" mkdir "saves"
pushd "%~dp0game"
start "" "%~dp0runtime\ruffle.exe" "Zinc Shell Mac.swf" %DISPLAY_ARGS% --quality high --scale show-all --force-scale --storage disk --save-directory "%~dp0saves" --config "%~dp0runtime\config" --cache-directory "%~dp0runtime\cache" --filesystem-access-mode allow --load-behavior delayed -P hdFPS=%FPS%
popd
