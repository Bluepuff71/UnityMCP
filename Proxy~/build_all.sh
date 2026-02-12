#!/bin/bash
set -e

# Navigate to script directory
cd "$(dirname "$0")"

echo "=========================================="
echo "Building UnityMCPProxy for all platforms"
echo "=========================================="

# Track build results
BUILD_RESULTS=""
FAILED=0

# Detect current platform
PLATFORM="unknown"
case "$(uname -s)" in
    Linux*)     PLATFORM="linux";;
    Darwin*)    PLATFORM="macos";;
    CYGWIN*|MINGW*|MSYS*) PLATFORM="windows";;
esac

echo "Detected platform: $PLATFORM"
echo ""

# Build for Linux
echo "------------------------------------------"
echo "Building for Linux..."
echo "------------------------------------------"
if [ "$PLATFORM" = "linux" ]; then
    if ./build_linux.sh; then
        BUILD_RESULTS="$BUILD_RESULTS\n  Linux: SUCCESS"
    else
        BUILD_RESULTS="$BUILD_RESULTS\n  Linux: FAILED"
        FAILED=1
    fi
else
    echo "Skipping Linux build (not on Linux platform)"
    BUILD_RESULTS="$BUILD_RESULTS\n  Linux: SKIPPED (not on Linux)"
fi

echo ""

# Build for macOS
echo "------------------------------------------"
echo "Building for macOS..."
echo "------------------------------------------"
if [ "$PLATFORM" = "macos" ]; then
    if ./build_macos.sh; then
        BUILD_RESULTS="$BUILD_RESULTS\n  macOS: SUCCESS"
    else
        BUILD_RESULTS="$BUILD_RESULTS\n  macOS: FAILED"
        FAILED=1
    fi
else
    echo "Skipping macOS build (not on macOS platform)"
    BUILD_RESULTS="$BUILD_RESULTS\n  macOS: SKIPPED (not on macOS)"
fi

echo ""

# Build for Windows
echo "------------------------------------------"
echo "Building for Windows..."
echo "------------------------------------------"
if [ "$PLATFORM" = "windows" ]; then
    # On Windows with Git Bash/MSYS, call the batch file
    if cmd.exe //c build_windows.bat; then
        BUILD_RESULTS="$BUILD_RESULTS\n  Windows: SUCCESS"
    else
        BUILD_RESULTS="$BUILD_RESULTS\n  Windows: FAILED"
        FAILED=1
    fi
else
    echo "Skipping Windows build (not on Windows platform)"
    BUILD_RESULTS="$BUILD_RESULTS\n  Windows: SKIPPED (not on Windows)"
fi

echo ""
echo "=========================================="
echo "Build Summary:"
echo -e "$BUILD_RESULTS"
echo "=========================================="

if [ $FAILED -eq 1 ]; then
    echo "One or more builds failed!"
    exit 1
fi

echo "All available platform builds completed successfully!"
exit 0
