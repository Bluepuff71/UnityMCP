#!/bin/bash
set -e

# Navigate to script directory
cd "$(dirname "$0")"

echo "Building UnityMCPProxy for macOS..."

# Create output directory if it doesn't exist
mkdir -p "../Plugins/macOS/"

# Check if clang is available
if ! command -v clang &> /dev/null; then
    echo "ERROR: clang compiler not found."
    echo "Please install Xcode Command Line Tools: xcode-select --install"
    exit 1
fi

# Build universal binary (arm64 + x86_64)
echo "Compiling universal binary (arm64 + x86_64)..."
clang -shared -O2 -DNDEBUG -DMG_ENABLE_LINES=0 \
    proxy.c mongoose.c \
    -o UnityMCPProxy.bundle \
    -arch arm64 -arch x86_64 \
    -framework CoreFoundation -framework Security

if [ ! -f "UnityMCPProxy.bundle" ]; then
    echo "ERROR: Compilation failed - output file not created"
    exit 1
fi

# Copy to Plugins folder
echo "Copying to Plugins folder..."
cp UnityMCPProxy.bundle ../Plugins/macOS/

echo "Build successful: UnityMCPProxy.bundle"
