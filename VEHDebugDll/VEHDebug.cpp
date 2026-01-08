#include "VEHDebug.h"
#include <tlhelp32.h>
#include <intrin.h>
#include <stdio.h>

// Global state
static HANDLE g_SharedMemHandle = NULL;
static VEHSharedMem* g_SharedMem = NULL;
static PVOID g_VEHHandle = NULL;
static bool g_Initialized = false;
// Note: Removed CRITICAL_SECTION - using lock-free operations instead

// Rate limiting to prevent overwhelming the game
static volatile LONG g_LastHitTime = 0;
static const LONG MIN_HIT_INTERVAL_MS = 1; // Minimum 1ms between recorded hits per thread

// Helper functions for safe handler tracking
static inline void EnterHandler() {
    if (g_SharedMem && g_SharedMem->Magic == VEH_SHARED_MEM_MAGIC) {
        InterlockedIncrement(&g_SharedMem->ActiveHandlers);
    }
}

static inline void LeaveHandler() {
    if (g_SharedMem && g_SharedMem->Magic == VEH_SHARED_MEM_MAGIC) {
        InterlockedDecrement(&g_SharedMem->ActiveHandlers);
    }
}

static inline bool ShouldProcess() {
    if (!g_SharedMem) return false;
    if (g_SharedMem->Magic != VEH_SHARED_MEM_MAGIC) return false;
    if (g_SharedMem->ShutdownRequested) return false;
    return true;
}

// Thread-local state for PAGE_GUARD single-step reapply
// Each thread tracks whether it's waiting to reapply the guard after a single-step
static __declspec(thread) bool tls_PendingGuardReapply = false;

// Forward declaration
LONG CALLBACK VectoredExceptionHandler(PEXCEPTION_POINTERS ExceptionInfo);

// Helper to set debug registers on current thread
static void SetDR7ForSlot(CONTEXT* ctx, int slot, DWORD type, DWORD size, bool enable)
{
    // DR7 layout per slot:
    // Slot 0: L0 at bit 0, RW0 at bits 16-17, LEN0 at bits 18-19
    // Slot 1: L1 at bit 2, RW1 at bits 20-21, LEN1 at bits 22-23
    // Slot 2: L2 at bit 4, RW2 at bits 24-25, LEN2 at bits 26-27
    // Slot 3: L3 at bit 6, RW3 at bits 28-29, LEN3 at bits 30-31

    int enableBit = slot * 2;
    int conditionBits = 16 + (slot * 4);
    int sizeBits = 18 + (slot * 4);

    // Map size to DR7 encoding
    DWORD sizeCode = 0;
    switch (size)
    {
    case 1: sizeCode = 0; break;  // 1 byte
    case 2: sizeCode = 1; break;  // 2 bytes
    case 4: sizeCode = 3; break;  // 4 bytes
    case 8: sizeCode = 2; break;  // 8 bytes (x64 only)
    default: sizeCode = 3; break; // Default to 4 bytes
    }

    // Clear existing bits for this slot
    ctx->Dr7 &= ~((DWORD64)3 << enableBit);
    ctx->Dr7 &= ~((DWORD64)3 << conditionBits);
    ctx->Dr7 &= ~((DWORD64)3 << sizeBits);

    if (enable)
    {
        // Set local enable
        ctx->Dr7 |= (DWORD64)1 << enableBit;
        // Set condition (type: 1 = write, 3 = read/write)
        ctx->Dr7 |= (DWORD64)type << conditionBits;
        // Set size
        ctx->Dr7 |= (DWORD64)sizeCode << sizeBits;
    }
}

LONG CALLBACK VectoredExceptionHandler(PEXCEPTION_POINTERS ExceptionInfo)
{
    DWORD exceptionCode = ExceptionInfo->ExceptionRecord->ExceptionCode;
    PCONTEXT ctx = ExceptionInfo->ContextRecord;

    // =====================================================
    // CRITICAL: Handle pending single-step FIRST, even during shutdown!
    // This MUST happen BEFORE the ShutdownRequested check, otherwise TF stays set
    // and causes infinite single-step exceptions -> crash
    // =====================================================
    if (exceptionCode == EXCEPTION_SINGLE_STEP || exceptionCode == 0x4000001E)
    {
        if (tls_PendingGuardReapply)
        {
            tls_PendingGuardReapply = false;

            // Clear trap flag IMMEDIATELY - this is critical to prevent infinite single-steps
            ctx->EFlags &= ~0x100;

            // Only reapply PAGE_GUARD if monitoring is still active AND not shutting down
            // Safe check for shared mem access
            if (g_Initialized && g_SharedMem)
            {
                __try 
                {
                    VEHSharedMem* sharedMem = g_SharedMem;
                    if (sharedMem && !sharedMem->ShutdownRequested)
                    {
                        volatile LONG magic = sharedMem->Magic;
                        volatile LONG active = sharedMem->Active;
                        volatile LONG usePageGuard = sharedMem->UsePageGuard;

                        if (magic == VEH_SHARED_MEM_MAGIC && active && usePageGuard)
                        {
                            DWORD64 pageBase = sharedMem->PageBase;
                            DWORD pageSize = sharedMem->PageSize;
                            DWORD origProt = sharedMem->OriginalProtection;

                            if (origProt != 0 && pageBase != 0 && pageSize != 0)
                            {
                                DWORD oldProtect;
                                VirtualProtect((LPVOID)pageBase, pageSize,
                                               origProt | 0x100,
                                               &oldProtect);
                            }
                        }
                    }
                }
                __except (EXCEPTION_EXECUTE_HANDLER)
                {
                    // access violation reading shared mem - ignore
                }
            }
            // TF is cleared, we're done - no need for EnterHandler/LeaveHandler tracking
            return EXCEPTION_CONTINUE_EXECUTION;
        }
    }

    // Early bail if not initialized
    // EARLY BAIL CHECKS
    // Critical: If we bail on a STATUS_GUARD_PAGE_VIOLATION (0x80000001), we MUST return
    // EXCEPTION_CONTINUE_EXECUTION to swallow the exception. If we return
    // EXCEPTION_CONTINUE_SEARCH, the OS sees an unhandled guard page violation and crashes the process.

    // 1. Not initialized
    if (!g_Initialized || !g_SharedMem)
    {
        if (exceptionCode == 0x80000001) return EXCEPTION_CONTINUE_EXECUTION;
        return EXCEPTION_CONTINUE_SEARCH;
    }

    // 2. Shutdown requested
    // Note: We need to check this safely. If g_SharedMem is invalid, this might crash.
    // So we'll defer this check until after we validate memory access.

    // Track that we're in a handler (for safe shutdown)
    EnterHandler();

    // Cache shared memory pointer
    VEHSharedMem* sharedMem = g_SharedMem;
    if (!sharedMem)
    {
        LeaveHandler();
        if (exceptionCode == 0x80000001) return EXCEPTION_CONTINUE_EXECUTION;
        return EXCEPTION_CONTINUE_SEARCH;
    }

    // 3. Validate shared memory access
    __try
    {
        // Check Magic
        if (sharedMem->Magic != VEH_SHARED_MEM_MAGIC)
        {
            LeaveHandler();
            if (exceptionCode == 0x80000001) return EXCEPTION_CONTINUE_EXECUTION;
            return EXCEPTION_CONTINUE_SEARCH;
        }

        // Check ShutdownRequested
        if (sharedMem->ShutdownRequested)
        {
            LeaveHandler();
            if (exceptionCode == 0x80000001) return EXCEPTION_CONTINUE_EXECUTION;
            return EXCEPTION_CONTINUE_SEARCH;
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        // Shared memory invalid/unmapped - bail out safely
        LeaveHandler();
        if (exceptionCode == 0x80000001) return EXCEPTION_CONTINUE_EXECUTION;
        return EXCEPTION_CONTINUE_SEARCH;
    }

    // =====================================================
    // PAGE_GUARD MODE: Handle STATUS_GUARD_PAGE_VIOLATION
    // =====================================================
    // STATUS_GUARD_PAGE_VIOLATION = 0x80000001
    // CRITICAL: We MUST handle this BEFORE checking 'Active' flag
    // The C# side might trigger PAGE_GUARD (via VirtualProtect) momentarily before setting Active=1
    // If we ignore it here because Active=0, the exception goes unhandled -> CRASH
    if (exceptionCode == 0x80000001)
    {
        __try
        {
            // Get the faulting address from ExceptionInformation[1]
            // ExceptionInformation[0] = read(0) or write(1)
            // ExceptionInformation[1] = the virtual address that was accessed
            DWORD64 faultAddr = (DWORD64)ExceptionInfo->ExceptionRecord->ExceptionInformation[1];

            // Read all shared memory values we need upfront
            DWORD64 pageBase = sharedMem->PageBase;
            DWORD pageSize = sharedMem->PageSize;

            // If pageBase or pageSize is 0, shared memory isn't fully configured yet
            // We MUST still handle this exception since PAGE_GUARD was consumed by Windows
            // Just continue execution without recording or setting TF
            if (pageBase == 0 || pageSize == 0)
            {
                LeaveHandler();
                return EXCEPTION_CONTINUE_EXECUTION;  // Let access proceed, guard is already gone
            }

            // Check if it's within our watched page
            // Use this strictly to decide ownership.
            if (faultAddr < pageBase || faultAddr >= pageBase + pageSize)
            {
                // Not our page - give it to other handlers (e.g. system stack growth)
                LeaveHandler();
                return EXCEPTION_CONTINUE_SEARCH;
            }

            // IT *IS* OUR PAGE.
            // We OWN this exception. We CANNOT return CONTINUE_SEARCH, or the app will crash.
            // We must return CONTINUE_EXECUTION to let the instruction retry (without guard)
            // or perform our logging logic.

            // If monitoring is disabled, shutting down, or guard mode is off:
            // Swaallow the exception and continue execution.
            if (sharedMem->ShutdownRequested || !sharedMem->Active || !sharedMem->UsePageGuard)
            {
                LeaveHandler();
                return EXCEPTION_CONTINUE_EXECUTION;
            }

            // PAGE_GUARD was triggered on our watched page and we are ACTIVE.
            // Process the hit.

            // Only RECORD hits if monitoring is fully active
            if (sharedMem->Active)
            {
                // Now check if this access is to our specific watched address
                DWORD64 watchAddr = sharedMem->WatchAddress;
                DWORD watchSize = sharedMem->BreakpointSize;
                if (watchSize == 0) watchSize = 4;  // Default to 4 bytes

                // Check if faultAddr overlaps with the watched address range [watchAddr, watchAddr+watchSize)
                // ExceptionInformation[1] for PAGE_GUARD gives the EXACT accessed address (not page-aligned)
                // We need to check if the accessed memory [faultAddr, faultAddr+accessSize) overlaps with
                // the watched memory [watchAddr, watchAddr+watchSize)
                // Since we don't know the exact access size, assume worst case of 8 bytes (QWORD access)
                const DWORD accessSize = 8;  // Assume max access size (QWORD)

                // Two ranges [A, A+sizeA) and [B, B+sizeB) overlap if: A < B+sizeB AND B < A+sizeA
                bool isRelevantHit = (faultAddr < watchAddr + watchSize) && (watchAddr < faultAddr + accessSize);

                if (isRelevantHit)
                {
                    // Get instruction pointer
#ifdef _WIN64
                    DWORD64 rip = ctx->Rip;
#else
                    DWORD64 rip = (DWORD64)ctx->Eip;
#endif
                    DWORD threadId = GetCurrentThreadId();

                    // Record the hit using lock-free atomic increment
                    LONG writeIdx = InterlockedIncrement(&sharedMem->WriteIndex) - 1;
                    writeIdx = (LONG)((ULONG)writeIdx % VEHSharedMem::MAX_HITS);

                    // Debug: Log to OutputDebugString
                    /*char dbgBuf[128];
                    sprintf_s(dbgBuf, "VEH HIT: EIP=0x%08X stored at idx=%d (offset=%d)\n",
                        (DWORD)rip, writeIdx, 80 + writeIdx * 8);
                    OutputDebugStringA(dbgBuf);*/

                    sharedMem->HitAddresses[writeIdx] = rip;
                    sharedMem->HitThreadIds[writeIdx] = threadId;
                    InterlockedIncrement(&sharedMem->HitCount);
                }
            }

            // Set trap flag (TF) to trigger single-step after this instruction completes
            // This allows us to safely reapply PAGE_GUARD after the memory access finishes
            // We MUST do this for ALL page accesses, not just our watched address,
            // otherwise the PAGE_GUARD won't be reapplied and we'll miss future hits
            ctx->EFlags |= 0x100;  // TF (Trap Flag) is bit 8

            // Mark this thread as needing to reapply guard on next single-step
            tls_PendingGuardReapply = true;

            // Continue execution - the guard is now removed so the access will proceed
            // After the instruction completes, we'll get EXCEPTION_SINGLE_STEP
            LeaveHandler();
            return EXCEPTION_CONTINUE_EXECUTION;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            // Shared memory became invalid during handling
            LeaveHandler();
            return EXCEPTION_CONTINUE_EXECUTION;
        }
    }

    // Check if monitoring is active - bail for HW breakpoints if not active
    if (!sharedMem->Active)
    {
        LeaveHandler();
        return EXCEPTION_CONTINUE_SEARCH;
    }

    if (!ctx)
    {
        LeaveHandler();
        return EXCEPTION_CONTINUE_SEARCH;
    }

    // Check DR6 - any of bits 0-3 indicate a hardware breakpoint
    // Bit 0 = DR0 triggered, Bit 1 = DR1, etc.
#ifdef _WIN64
    DWORD64 dr6 = ctx->Dr6;
#else
    DWORD dr6 = ctx->Dr6;
#endif

    // Check if ANY hardware breakpoint triggered (bits 0-3)
    if (!(dr6 & 0xF))
    {
        // Not a hardware breakpoint - might be single step from debugger or TF flag
        LeaveHandler();
        return EXCEPTION_CONTINUE_SEARCH;
    }

    // Check if our specific slot triggered
    int slot = (int)sharedMem->BreakpointSlot;
    if (slot < 0 || slot > 3)
        slot = 0;

    DWORD slotMask = (1 << slot);

    if (dr6 & slotMask)
    {
        // Our breakpoint was hit!
        // Get instruction pointer
#ifdef _WIN64
        DWORD64 rip = ctx->Rip;
#else
        DWORD64 rip = ctx->Eip;
#endif

        // RATE LIMITING: Only record hits every few milliseconds to prevent
        // overwhelming the system when an address is accessed in a tight loop
        DWORD currentTime = GetTickCount();
        LONG lastTime = g_LastHitTime;
        if ((currentTime - (DWORD)lastTime) < 5) // 5ms minimum between recorded hits
        {
            // Skip recording but still continue execution properly
            ctx->Dr6 = 0;
            ctx->EFlags |= 0x10000;
            LeaveHandler();
            return EXCEPTION_CONTINUE_EXECUTION;
        }
        InterlockedExchange(&g_LastHitTime, (LONG)currentTime);

        DWORD threadId = GetCurrentThreadId();

        // LOCK-FREE HIT RECORDING:
        // Use InterlockedIncrement to atomically grab a unique write slot.
        // This eliminates all lock contention - each thread gets its own slot.
        // No deadlocks possible, no dropped hits from failed TryEnterCriticalSection.
        LONG writeIdx = InterlockedIncrement(&sharedMem->WriteIndex) - 1;
        writeIdx = writeIdx % VEHSharedMem::MAX_HITS;

        // Write hit data - no lock needed since each thread has unique slot
        sharedMem->HitAddresses[writeIdx] = rip;
        sharedMem->HitThreadIds[writeIdx] = threadId;

        // Increment total hit count
        InterlockedIncrement(&sharedMem->HitCount);

        // Clear DR6 completely to acknowledge all debug events
        ctx->Dr6 = 0;

        // Set Resume Flag (RF) bit 16 - this causes the processor to ignore
        // debug faults for the next instruction only
        ctx->EFlags |= 0x10000;

        LeaveHandler();
        return EXCEPTION_CONTINUE_EXECUTION;
    }

    // A different hardware breakpoint triggered, not ours
    // Clear our bit if it was somehow set and let other handlers deal with it
    LeaveHandler();
    return EXCEPTION_CONTINUE_SEARCH;
}

BOOL __stdcall InitializeVEH(const wchar_t* sharedMemName)
{
    if (!sharedMemName || wcslen(sharedMemName) == 0)
        return FALSE;

    // If already initialized, do a full cleanup first to ensure clean state
    // This prevents crashes from leftover state between monitoring sessions
    if (g_Initialized || g_VEHHandle || g_SharedMem)
    {
        OutputDebugStringA("VEHDebug: Previous state detected, doing full cleanup first\n");
        UninitializeVEH();
    }

    OutputDebugStringW(L"VEHDebug: Initializing with shared memory: ");
    OutputDebugStringW(sharedMemName);
    OutputDebugStringW(L"\n");

    // Open the shared memory created by CrxMem
    g_SharedMemHandle = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, sharedMemName);
    if (!g_SharedMemHandle)
    {
        DWORD err = GetLastError();
        char buf[128];
        sprintf_s(buf, "VEHDebug: Failed to open shared memory, error: %lu\n", err);
        OutputDebugStringA(buf);
        return FALSE;
    }

    g_SharedMem = (VEHSharedMem*)MapViewOfFile(g_SharedMemHandle, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(VEHSharedMem));
    if (!g_SharedMem)
    {
        CloseHandle(g_SharedMemHandle);
        g_SharedMemHandle = NULL;
        return FALSE;
    }

    // Register the VEH handler only if not already registered
    if (!g_VEHHandle)
    {
        g_VEHHandle = AddVectoredExceptionHandler(1, VectoredExceptionHandler);
        if (!g_VEHHandle)
        {
            UnmapViewOfFile(g_SharedMem);
            CloseHandle(g_SharedMemHandle);
            g_SharedMem = NULL;
            g_SharedMemHandle = NULL;
            return FALSE;
        }
    }

    OutputDebugStringA("VEHDebug: Initialized successfully\n");
    g_Initialized = true;
    return TRUE;
}

void __stdcall UninitializeVEH()
{
    if (!g_Initialized)
        return;

    OutputDebugStringA("VEHDebug: Uninitializing\n");

    // Remove VEH handler
    if (g_VEHHandle)
    {
        RemoveVectoredExceptionHandler(g_VEHHandle);
        g_VEHHandle = NULL;
    }

    // Clear any active breakpoints on all threads
    for (int slot = 0; slot < 4; slot++)
    {
        ClearHardwareBreakpoint(slot);
    }

    // Unmap shared memory
    if (g_SharedMem)
    {
        InterlockedExchange(&g_SharedMem->Active, 0);
        UnmapViewOfFile(g_SharedMem);
        g_SharedMem = NULL;
    }

    if (g_SharedMemHandle)
    {
        CloseHandle(g_SharedMemHandle);
        g_SharedMemHandle = NULL;
    }

    g_Initialized = false;
}

// Set hardware breakpoint on all threads in the process
BOOL __stdcall SetHardwareBreakpoint(int slot, DWORD64 address, DWORD type, DWORD size)
{
    if (slot < 0 || slot > 3)
        return FALSE;

    DWORD processId = GetCurrentProcessId();
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snapshot == INVALID_HANDLE_VALUE)
        return FALSE;

    THREADENTRY32 te;
    te.dwSize = sizeof(te);

    BOOL success = TRUE;
    DWORD currentThreadId = GetCurrentThreadId();

    if (Thread32First(snapshot, &te))
    {
        do
        {
            if (te.th32OwnerProcessID == processId)
            {
                HANDLE hThread = OpenThread(THREAD_GET_CONTEXT | THREAD_SET_CONTEXT | THREAD_SUSPEND_RESUME,
                    FALSE, te.th32ThreadID);
                if (hThread)
                {
                    // Don't suspend current thread
                    if (te.th32ThreadID != currentThreadId)
                        SuspendThread(hThread);

                    CONTEXT ctx;
                    ctx.ContextFlags = CONTEXT_DEBUG_REGISTERS;

                    if (GetThreadContext(hThread, &ctx))
                    {
                        // Set the address in the appropriate DR register
                        switch (slot)
                        {
                        case 0: ctx.Dr0 = address; break;
                        case 1: ctx.Dr1 = address; break;
                        case 2: ctx.Dr2 = address; break;
                        case 3: ctx.Dr3 = address; break;
                        }

                        // Configure DR7
                        SetDR7ForSlot(&ctx, slot, type, size, true);

                        // Clear DR6
                        ctx.Dr6 = 0;

                        if (!SetThreadContext(hThread, &ctx))
                            success = FALSE;
                    }
                    else
                    {
                        success = FALSE;
                    }

                    if (te.th32ThreadID != currentThreadId)
                        ResumeThread(hThread);

                    CloseHandle(hThread);
                }
            }
            te.dwSize = sizeof(te);
        } while (Thread32Next(snapshot, &te));
    }

    CloseHandle(snapshot);
    return success;
}

void __stdcall ClearHardwareBreakpoint(int slot)
{
    if (slot < 0 || slot > 3)
        return;

    DWORD processId = GetCurrentProcessId();
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snapshot == INVALID_HANDLE_VALUE)
        return;

    THREADENTRY32 te;
    te.dwSize = sizeof(te);

    DWORD currentThreadId = GetCurrentThreadId();

    if (Thread32First(snapshot, &te))
    {
        do
        {
            if (te.th32OwnerProcessID == processId)
            {
                HANDLE hThread = OpenThread(THREAD_GET_CONTEXT | THREAD_SET_CONTEXT | THREAD_SUSPEND_RESUME,
                    FALSE, te.th32ThreadID);
                if (hThread)
                {
                    if (te.th32ThreadID != currentThreadId)
                        SuspendThread(hThread);

                    CONTEXT ctx;
                    ctx.ContextFlags = CONTEXT_DEBUG_REGISTERS;

                    if (GetThreadContext(hThread, &ctx))
                    {
                        // Clear the address register
                        switch (slot)
                        {
                        case 0: ctx.Dr0 = 0; break;
                        case 1: ctx.Dr1 = 0; break;
                        case 2: ctx.Dr2 = 0; break;
                        case 3: ctx.Dr3 = 0; break;
                        }

                        // Disable in DR7
                        SetDR7ForSlot(&ctx, slot, 0, 0, false);

                        SetThreadContext(hThread, &ctx);
                    }

                    if (te.th32ThreadID != currentThreadId)
                        ResumeThread(hThread);

                    CloseHandle(hThread);
                }
            }
            te.dwSize = sizeof(te);
        } while (Thread32Next(snapshot, &te));
    }

    CloseHandle(snapshot);
}

// RefreshBreakpoints - Re-apply breakpoints to all threads
// Call this periodically from C# to catch any new threads created after initial setup
BOOL __stdcall RefreshBreakpoints()
{
    if (!g_Initialized || !g_SharedMem)
        return FALSE;

    // Only refresh if monitoring is active
    if (!g_SharedMem->Active)
        return FALSE;

    // Re-apply the breakpoint to all current threads
    return SetHardwareBreakpoint(
        (int)g_SharedMem->BreakpointSlot,
        g_SharedMem->WatchAddress,
        g_SharedMem->BreakpointType,
        g_SharedMem->BreakpointSize
    );
}

// Helper to set breakpoint on a single thread
static void SetBreakpointOnCurrentThread()
{
    if (!g_Initialized || !g_SharedMem || !g_SharedMem->Active)
        return;

    CONTEXT ctx;
    ctx.ContextFlags = CONTEXT_DEBUG_REGISTERS;

    // Get current thread context
    HANDLE hThread = GetCurrentThread();
    if (!GetThreadContext(hThread, &ctx))
        return;

    int slot = (int)g_SharedMem->BreakpointSlot;
    if (slot < 0 || slot > 3)
        slot = 0;

    // Set the address in the appropriate DR register
    switch (slot)
    {
    case 0: ctx.Dr0 = g_SharedMem->WatchAddress; break;
    case 1: ctx.Dr1 = g_SharedMem->WatchAddress; break;
    case 2: ctx.Dr2 = g_SharedMem->WatchAddress; break;
    case 3: ctx.Dr3 = g_SharedMem->WatchAddress; break;
    }

    // Configure DR7
    SetDR7ForSlot(&ctx, slot, g_SharedMem->BreakpointType, g_SharedMem->BreakpointSize, true);

    // Clear DR6
    ctx.Dr6 = 0;

    SetThreadContext(hThread, &ctx);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        // Don't disable thread notifications - we need them to catch new threads
        OutputDebugStringA("VEHDebug: DLL attached\n");
        break;
    case DLL_THREAD_ATTACH:
        // New thread created - set breakpoint on it automatically
        // This happens INSIDE the new thread's context, so we can set its DR registers directly
        SetBreakpointOnCurrentThread();
        break;
    case DLL_PROCESS_DETACH:
        OutputDebugStringA("VEHDebug: DLL detaching\n");
        UninitializeVEH();
        break;
    }
    return TRUE;
}
