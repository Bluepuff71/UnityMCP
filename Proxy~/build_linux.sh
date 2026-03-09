#!/bin/bash
set -e

# Navigate to script directory
cd "$(dirname "$0")"

echo "Building UnityMCPProxy for Linux..."

# Create output directory if it doesn't exist
mkdir -p "../Package/Plugins/Linux/x86_64/"

# Check if gcc is available
if ! command -v gcc &> /dev/null; then
    echo "ERROR: gcc compiler not found."
    echo "Please install gcc: sudo apt-get install build-essential"
    exit 1
fi

# Build shared library
echo "Compiling shared library..."
gcc -shared -fPIC -O2 -DNDEBUG -DMG_ENABLE_LINES=0 -DMG_TLS=MG_TLS_BUILTIN \
    proxy.c mongoose.c \
    -o libUnityMCPProxy.so \
    -lpthread

if [ ! -f "libUnityMCPProxy.so" ]; then
    echo "ERROR: Compilation failed - output file not created"
    exit 1
fi

# Copy to Package/Plugins folder (this is where Unity actually loads from)
echo "Copying to Package/Plugins folder..."
mkdir -p "../Package/Plugins/Linux/x86_64/"
cp libUnityMCPProxy.so ../Package/Plugins/Linux/x86_64/

echo "Build successful: libUnityMCPProxy.so"
