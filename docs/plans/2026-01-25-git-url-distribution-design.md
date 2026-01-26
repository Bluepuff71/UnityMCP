# Git URL Distribution Design

## Overview

Migrate from tarball releases to Git URL installation via Unity Package Manager.

## Repository Structure

```
UnityMCP/
├── Package/                    # UPM package
│   ├── package.json
│   ├── README.md
│   ├── LICENSE
│   ├── Editor/
│   ├── Runtime/
│   └── Plugins/
│       ├── Windows/x86_64/UnityMCPProxy.dll
│       ├── macOS/UnityMCPProxy.bundle
│       └── Linux/x86_64/libUnityMCPProxy.so
├── NativeProxy~/               # Build scripts, source
├── docs/                       # Internal docs
├── Tests~/                     # Tests
└── .github/workflows/          # CI/CD
```

## Installation

Users install via:
```
https://github.com/Bluepuff71/UnityMCP.git?path=/Package
```

Pin version with:
```
https://github.com/Bluepuff71/UnityMCP.git?path=/Package#2026.01.26
```

## GitHub Actions Workflow

1. Build native plugins on Windows, macOS, Linux
2. Download artifacts into `Package/Plugins/`
3. Update `Package/package.json` version (CalVer)
4. Commit changes to repo
5. Create git tag
6. Push commit and tag

No tarball creation needed.

## README Changes

- Installation via "Add package from git URL"
- Version pinning with `#tag` syntax
- Releases page lists available versions
