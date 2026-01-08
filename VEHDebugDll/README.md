# VEHDebugDll - Debugging Library

> **Part of the [CrxMem Suite](../README.md)**

A Vectored Exception Handler (VEH) based debugging DLL for Windows. Enables usermode debugging through exception handling for the CrxMem memory scanner.

---

## IMPORTANT: Project Status

> **This project is under active development.** Some features may be incomplete or unstable.

---

## Features

- **VEH-Based Debugging** - Usermode debugging without requiring a debugger attachment
- **Hardware Breakpoints** - Utilize x86/x64 debug registers (DR0-DR3)
- **Memory Access Monitoring** - Track reads and writes to specific addresses
- **Shared Memory Communication** - Fast IPC with CrxMem via shared memory
- **Lock-Free Operations** - Thread-safe without heavy synchronization
- **Rate Limiting** - Prevent overwhelming the target process

---

## How It Works

VEHDebugDll uses Windows Vectored Exception Handling to intercept:

1. **Hardware Breakpoint Exceptions** (`EXCEPTION_SINGLE_STEP`)
   - Triggered by debug registers DR0-DR3
   - Monitors memory access patterns

2. **Page Guard Exceptions** (`STATUS_GUARD_PAGE_VIOLATION`)
   - Alternative monitoring method
   - Detects access to protected memory pages

Communication with CrxMem occurs through shared memory, allowing real-time monitoring without performance-heavy IPC mechanisms.

---

## Building

### Requirements
- Visual Studio 2022 with C++ workload
- CMake 3.15+ (optional, for CMake builds)
- Windows SDK

### Visual Studio Build
1. Open `VEHDebug64.vcxproj` in Visual Studio
2. Select Release x64 configuration
3. Build the solution
4. Output: `bin/Release/VEHDebug64.dll`

### CMake Build
```bash
mkdir build64 && cd build64
cmake .. -A x64
cmake --build . --config Release
```

---

## API Reference

### Exported Functions

```cpp
// Initialize the VEH debugging system
// Returns: TRUE on success, FALSE on failure
BOOL __declspec(dllexport) VEH_Initialize();

// Shutdown and cleanup
void __declspec(dllexport) VEH_Shutdown();

// Set a hardware breakpoint
// slot: 0-3 (debug register)
// address: Target address to monitor
// type: 0=execute, 1=write, 3=read/write
// size: 1, 2, 4, or 8 bytes
BOOL __declspec(dllexport) VEH_SetBreakpoint(int slot, ULONG_PTR address, DWORD type, DWORD size);

// Clear a hardware breakpoint
BOOL __declspec(dllexport) VEH_ClearBreakpoint(int slot);

// Get shared memory pointer for direct access
VEHSharedMem* __declspec(dllexport) VEH_GetSharedMemory();
```

---

## Shared Memory Structure

```cpp
struct VEHSharedMem {
    DWORD Magic;              // Validation magic number
    DWORD Version;            // Structure version
    volatile LONG ShutdownRequested;
    volatile LONG ActiveHandlers;

    // Breakpoint configuration
    struct {
        ULONG_PTR Address;
        DWORD Type;
        DWORD Size;
        BOOL Enabled;
    } Breakpoints[4];

    // Hit recording
    volatile LONG HitCount;
    struct {
        ULONG_PTR Address;
        ULONG_PTR InstructionPointer;
        DWORD ThreadId;
        DWORD Type;  // Read/Write/Execute
    } Hits[MAX_HITS];
};
```

---

## Usage Example

```cpp
// In the injected DLL or target process
#include "VEHDebug.h"

// Initialize
if (VEH_Initialize()) {
    // Set breakpoint on address 0x12345678, monitor writes, 4 bytes
    VEH_SetBreakpoint(0, 0x12345678, 1, 4);

    // ... let the program run ...

    // Check hits from shared memory
    VEHSharedMem* mem = VEH_GetSharedMemory();
    if (mem && mem->HitCount > 0) {
        // Process recorded hits
    }

    // Cleanup
    VEH_ClearBreakpoint(0);
    VEH_Shutdown();
}
```

---

## Project Structure

```
VEHDebugDll/
├── VEHDebug.cpp           # Main implementation
├── VEHDebug.h             # Header with API definitions
├── VEHDebug.def           # DLL exports definition
├── VEHDebug64.vcxproj     # VS project for 64-bit
├── VEHDebug32.vcxproj     # VS project for 32-bit
├── CMakeLists.txt         # CMake build file
└── build.bat              # Build helper script
```

---

## Related Projects

| Project | Description |
|---------|-------------|
| [CrxMem](https://github.com/ZxPwdz/CrxMem) | Main memory scanner application |
| [CrxShield](https://github.com/ZxPwdz/CrxShield) | Kernel driver for enhanced access |

---

## Technical Notes

### Hardware Breakpoints (DR0-DR3)
- Limited to 4 simultaneous breakpoints
- Support different access types:
  - `00` = Execute
  - `01` = Write only
  - `11` = Read/Write
- Size encoding in DR7:
  - `00` = 1 byte
  - `01` = 2 bytes
  - `11` = 4 bytes
  - `10` = 8 bytes (64-bit only)

### Thread Safety
- Uses `InterlockedIncrement`/`InterlockedDecrement` for reference counting
- Lock-free hit recording for minimal performance impact
- Thread-local storage for pending guard reapply tracking

---

## Disclaimer

**This software is provided for educational and security research purposes only.**

- Do NOT use this library for malicious purposes
- Do NOT use this to bypass anti-cheat systems
- The author is NOT responsible for any misuse

---

## License

MIT License - For educational use only.

---

**Created by ZxPwdz**
