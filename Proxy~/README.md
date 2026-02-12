# UnityMCP Proxy Plugin

C source code for UnityMCP's domain-reload-resistant HTTP server.

## Purpose

Unity's managed C# code is unloaded during domain reloads (entering/exiting Play mode, script recompilation). This plugin provides an HTTP server that persists across these reloads, allowing continuous communication with MCP clients.

## Folder Naming

The `~` suffix in `Proxy~` tells Unity to ignore this folder entirely. Unity will not attempt to import or process any files within it. This is intentional because:
- Unity cannot compile C source files
- We need to build these into platform-specific libraries externally

## Contents

- `mongoose.c` / `mongoose.h` - The Mongoose embedded HTTP library (https://github.com/cesanta/mongoose)
- `proxy.c` / `proxy.h` - UnityMCP proxy server implementation

## Build Instructions

### Windows (x86_64)

```bash
# Using MSVC (Visual Studio Developer Command Prompt)
cl /LD /O2 /DMG_ENABLE_LINES=0 proxy.c mongoose.c /Fe:proxy.dll

# Or using MinGW
gcc -shared -O2 -DMG_ENABLE_LINES=0 proxy.c mongoose.c -o proxy.dll -lws2_32
```

### macOS (Universal Binary)

```bash
# Build for both architectures
clang -dynamiclib -O2 -DMG_ENABLE_LINES=0 proxy.c mongoose.c -o proxy.dylib -arch x86_64 -arch arm64

# Create .bundle for Unity
mkdir -p proxy.bundle/Contents/MacOS
cp proxy.dylib proxy.bundle/Contents/MacOS/proxy
```

### Linux (x86_64)

```bash
gcc -shared -fPIC -O2 -DMG_ENABLE_LINES=0 proxy.c mongoose.c -o libproxy.so
```

## Output Locations

Built libraries should be placed in:
- Windows: `../Plugins/Windows/x86_64/proxy.dll`
- macOS: `../Plugins/macOS/proxy.bundle`
- Linux: `../Plugins/Linux/x86_64/libproxy.so`

## Mongoose Library

Mongoose is a lightweight embedded web server library. We use it because:
- Single-file distribution (easy to include)
- No external dependencies
- Cross-platform support
- MIT licensed

Source: https://github.com/cesanta/mongoose
