#pragma once

#include <windows.h>

// Magic number to validate shared memory is still valid
#define VEH_SHARED_MEM_MAGIC 0x56454844  // "VEHD" in little-endian

// Shared memory structure for communication between CrxMem and the injected DLL
// Uses LONG types for fields that need atomic operations (InterlockedIncrement)
#pragma pack(push, 1)
struct VEHSharedMem
{
    // Magic number for validation - MUST be first field
    volatile LONG Magic;                // Should be VEH_SHARED_MEM_MAGIC when valid

    // Control flags - use LONG for Interlocked* compatibility
    volatile LONG Active;               // 1 = monitoring active, 0 = stop
    volatile LONG HitCount;             // Total number of hits

    // Breakpoint configuration
    DWORD64 WatchAddress;               // Address being monitored
    DWORD BreakpointSize;               // 1, 2, 4, or 8
    DWORD BreakpointType;               // 1 = Write only, 3 = Read/Write
    DWORD BreakpointSlot;               // Which DR register (0-3)

    // Hit buffer - circular buffer of instruction pointers that hit the breakpoint
    static const int MAX_HITS = 1024;
    volatile LONG WriteIndex;           // Next write position (LONG for InterlockedIncrement)
    LONG ReadIndex;                     // Next read position (updated by C# side only)

    // PAGE_GUARD mode fields (alternative to hardware breakpoints)
    volatile LONG UsePageGuard;         // 1 = PAGE_GUARD mode, 0 = HW BP mode
    volatile LONG NeedReapplyGuard;     // (Deprecated - DLL handles reapply via single-step)
    DWORD64 PageBase;                   // Base address of watched page (4KB aligned)
    DWORD PageSize;                     // Size of page (usually 4096)
    DWORD OriginalProtection;           // Original page protection before PAGE_GUARD was added

    // NEW: Synchronization fields for safe shutdown
    volatile LONG ActiveHandlers;       // Count of VEH handlers currently executing
    volatile LONG ShutdownRequested;    // 1 when shutdown in progress
    DWORD Reserved[2];                  // Reserved for future use (alignment to 80 bytes header)

    DWORD64 HitAddresses[MAX_HITS];     // RIP/EIP values
    DWORD HitThreadIds[MAX_HITS];       // Thread IDs

    // Events (handles duplicated for target process)
    HANDLE StopEvent;                   // Signaled when monitoring should stop
};
#pragma pack(pop)

// Compile-time verification of struct layout
// Header should be 80 bytes, HitAddresses at offset 80
static_assert(offsetof(VEHSharedMem, HitAddresses) == 80, "HitAddresses offset mismatch!");
static_assert(offsetof(VEHSharedMem, HitThreadIds) == 80 + (1024 * 8), "HitThreadIds offset mismatch!");

// Export functions
extern "C" {
    __declspec(dllexport) BOOL __stdcall InitializeVEH(const wchar_t* sharedMemName);
    __declspec(dllexport) void __stdcall UninitializeVEH();
    __declspec(dllexport) BOOL __stdcall SetHardwareBreakpoint(int slot, DWORD64 address, DWORD type, DWORD size);
    __declspec(dllexport) void __stdcall ClearHardwareBreakpoint(int slot);
    __declspec(dllexport) BOOL __stdcall RefreshBreakpoints();
}
