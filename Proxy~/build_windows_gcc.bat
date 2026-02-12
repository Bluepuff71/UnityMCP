@echo off
setlocal enabledelayedexpansion

:: Navigate to script directory
cd /d "%~dp0"

echo Building UnityMCPProxy for Windows using GCC (MinGW)...

:: Create output directory if it doesn't exist
if not exist "..\Plugins\Windows\x86_64\" (
    mkdir "..\Plugins\Windows\x86_64\"
    if errorlevel 1 (
        echo ERROR: Failed to create output directory
        exit /b 1
    )
)

:: Check if GCC compiler is available
where gcc >nul 2>&1
if errorlevel 1 (
    echo ERROR: GCC compiler not found.
    echo Please install MinGW-w64 or MSYS2 and add to PATH.
    exit /b 1
)

:: Build with GCC
echo Compiling...
gcc -shared -O2 -DNDEBUG -DMG_ENABLE_LINES=0 ^
    proxy.c mongoose.c ^
    -o UnityMCPProxy.dll ^
    -lws2_32 ^
    -Wl,--out-implib,libUnityMCPProxy.a

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
if exist libUnityMCPProxy.a del libUnityMCPProxy.a

echo Build successful: UnityMCPProxy.dll
exit /b 0
