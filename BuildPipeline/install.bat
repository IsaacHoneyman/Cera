@echo off
setlocal EnableDelayedExpansion

echo Installing Cera Compiler __VERSION__ for Windows...

:: Define target Windows directory
set "INSTALL_DIR=%LOCALAPPDATA%\Cera"
set "BIN_DIR=%INSTALL_DIR%\bin"

:: 1. Create the installation directory if it doesn't exist
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

:: 2. Copy the distribution files to the installation directory
xcopy /E /I /Y ".\bin" "%INSTALL_DIR%\bin"
xcopy /E /I /Y ".\lib" "%INSTALL_DIR%\lib"

echo Files copied successfully to %INSTALL_DIR%.

:: 3. Check if the Cera bin directory is already in the user's PATH
:: We query the registry directly to avoid the string length limits of %PATH%
for /f "tokens=2*" %%A in ('reg query HKCU\Environment /v PATH 2^>nul') do set "USER_PATH=%%B"

echo %USER_PATH% | find /i "%BIN_DIR%" > nul
if %ERRORLEVEL% == 0 (
    echo Cera is already in your system PATH.
) else (
    echo Adding Cera to your system PATH...
    :: Append the Cera bin directory to the permanent user PATH
    setx PATH "%USER_PATH%;%BIN_DIR%"
    echo Please restart your Command Prompt or PowerShell for the PATH changes to take effect.
)

echo Installation complete!
pause