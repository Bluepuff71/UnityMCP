@echo off
setlocal enabledelayedexpansion

:: Navigate to script directory
cd /d "%~dp0"

echo Building UnityMCPProxy for Windows...

:: Create output directory if it doesn't exist
if not exist "..\Plugins\Windows\x86_64\" (
    mkdir "..\Plugins\Windows\x86_64\"
    if errorlevel 1 (
        echo ERROR: Failed to create output directory
        exit /b 1
    )
)

:: Check if MSVC compiler is available
where cl >nul 2>&1
if errorlevel 1 (
    echo ERROR: MSVC compiler (cl.exe) not found.
    echo Please run this script from a Visual Studio Developer Command Prompt.
    exit /b 1
)

:: Build with MSVC
echo Compiling...
cl /LD /O2 /DNDEBUG /DMG_ENABLE_LINES=0 proxy.c mongoose.c /Fe:UnityMCPProxy.dll ws2_32.lib
if errorlevel 1 (
    echo ERROR: Compilation failed
    exit /b 1
)

:: Copy to Plugins folder
echo Copying to Plugins folder...
copy /Y UnityMCPProxy.dll "..\Plugins\Windows\x86_64\"
if errorlevel 1 (
    echo ERROR: Failed to copy DLL to Plugins folder
    exit /b 1
)

:: Clean up intermediate files
if exist UnityMCPProxy.exp del UnityMCPProxy.exp
if exist UnityMCPProxy.lib del UnityMCPProxy.lib
if exist UnityMCPProxy.obj del UnityMCPProxy.obj
if exist proxy.obj del proxy.obj
if exist mongoose.obj del mongoose.obj

echo Build successful: UnityMCPProxy.dll
exit /b 0
