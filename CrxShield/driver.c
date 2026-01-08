/*
CrxShield - Enhanced Security Research Kernel Driver
Purpose: Test kernel driver for anticheat bypass research
This driver is for testing your own anticheat systems only.
Stealth Improvements:
- String obfuscation
- Driver and device hiding
- Callback enumeration and removal
- Reduced logging
WARNING: Kernel mode code can cause system instability (BSOD).
Always test on a system you can afford to crash.
*/

#include <ntifs.h>      // Provides LIST_ENTRY and other kernel internals
#include <ntimage.h>

// Manual definition of LDR_DATA_TABLE_ENTRY (undocumented, standard RE layout for DKOM)
typedef struct _LDR_DATA_TABLE_ENTRY {
    LIST_ENTRY InLoadOrderLinks;
    LIST_ENTRY InMemoryOrderLinks;
    LIST_ENTRY InInitializationOrderLinks;
    PVOID      DllBase;
    PVOID      EntryPoint;
    ULONG      SizeOfImage;
    UNICODE_STRING FullDllName;
    UNICODE_STRING BaseDllName;
    ULONG      Flags;
    USHORT     LoadCount;
    USHORT     TlsIndex;
    LIST_ENTRY HashLinks;
    ULONG      TimeDateStamp;
    // Additional fields vary by OS version - the above is sufficient for hiding
} LDR_DATA_TABLE_ENTRY, * PLDR_DATA_TABLE_ENTRY;

// Logging Macro
#define LOG(fmt, ...) DbgPrintEx(DPFLTR_SYSTEM_ID, DPFLTR_ERROR_LEVEL, "[CrxMemKernel] " fmt "\n", __VA_ARGS__)

// Obfuscated strings (simple XOR encryption, key 0xAA)
#define XOR_KEY 0xAA
#define OBFUSCATE_STRING(str) { \
    char* p = str; \
    while (*p) { *p ^= XOR_KEY; p++; } \
}

char g_ObfDeviceName[] = { '\\' ^ XOR_KEY, 'D' ^ XOR_KEY, 'e' ^ XOR_KEY, 'v' ^ XOR_KEY, 'i' ^ XOR_KEY, 'c' ^ XOR_KEY, 'e' ^ XOR_KEY, '\\' ^ XOR_KEY, 'C' ^ XOR_KEY, 'r' ^ XOR_KEY, 'x' ^ XOR_KEY, 'S' ^ XOR_KEY, 'h' ^ XOR_KEY, 'i' ^ XOR_KEY, 'e' ^ XOR_KEY, 'l' ^ XOR_KEY, 'd' ^ XOR_KEY, 0 };
char g_ObfSymLinkName[] = { '\\' ^ XOR_KEY, 'D' ^ XOR_KEY, 'o' ^ XOR_KEY, 's' ^ XOR_KEY, 'D' ^ XOR_KEY, 'e' ^ XOR_KEY, 'v' ^ XOR_KEY, 'i' ^ XOR_KEY, 'c' ^ XOR_KEY, 'e' ^ XOR_KEY, 's' ^ XOR_KEY, '\\' ^ XOR_KEY, 'C' ^ XOR_KEY, 'r' ^ XOR_KEY, 'x' ^ XOR_KEY, 'S' ^ XOR_KEY, 'h' ^ XOR_KEY, 'i' ^ XOR_KEY, 'e' ^ XOR_KEY, 'l' ^ XOR_KEY, 'd' ^ XOR_KEY, 0 };

// IOCTL codes (randomized base to avoid signatures)
#define IOCTL_BASE 0x1337
#define IOCTL_CRXSHIELD_GET_VERSION     CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x0, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_CRXSHIELD_READ_MEMORY     CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x1, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_CRXSHIELD_WRITE_MEMORY    CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x2, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_CRXSHIELD_GET_PROCESS_BASE CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x3, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_CRXSHIELD_ENUM_CALLBACKS  CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x4, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_CRXSHIELD_REMOVE_CALLBACK CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x5, METHOD_BUFFERED, FILE_ANY_ACCESS)

// Driver version
#define CRXSHIELD_VERSION_MAJOR 1
#define CRXSHIELD_VERSION_MINOR 1
#define CRXSHIELD_VERSION_BUILD 0

// Structures for IOCTL communication
#pragma pack(push, 1)
typedef struct _CRXSHIELD_VERSION {
    ULONG Major;
    ULONG Minor;
    ULONG Build;
} CRXSHIELD_VERSION, * PCRXSHIELD_VERSION;

typedef struct _CRXSHIELD_READ_REQUEST {
    ULONG ProcessId;
    ULONG64 Address;
    ULONG Size;
} CRXSHIELD_READ_REQUEST, * PCRXSHIELD_READ_REQUEST;

typedef struct _CRXSHIELD_WRITE_REQUEST {
    ULONG ProcessId;
    ULONG64 Address;
    ULONG Size;
} CRXSHIELD_WRITE_REQUEST, * PCRXSHIELD_WRITE_REQUEST;

typedef struct _CRXSHIELD_PROCESS_BASE_REQUEST {
    ULONG ProcessId;
    ULONG64 BaseAddress;
} CRXSHIELD_PROCESS_BASE_REQUEST, * PCRXSHIELD_PROCESS_BASE_REQUEST;

// Placeholder for ObCallbackEntry (common layout - adjust per Windows version)
typedef struct _OB_CALLBACK_ENTRY {
    LIST_ENTRY Entry;
    PVOID Callback;
    PVOID Context;
    // Additional fields exist in real structure
} OB_CALLBACK_ENTRY, * POB_CALLBACK_ENTRY;

typedef struct _CRXSHIELD_CALLBACK_ENTRY {
    ULONG64 CallbackAddress;
    ULONG64 Context;
} CRXSHIELD_CALLBACK_ENTRY, * PCRXSHIELD_CALLBACK_ENTRY;

typedef struct _CRXSHIELD_ENUM_CALLBACKS_REQUEST {
    ULONG MaxEntries;
    ULONG EntryCount;
} CRXSHIELD_ENUM_CALLBACKS_REQUEST, * PCRXSHIELD_ENUM_CALLBACKS_REQUEST;

typedef struct _CRXSHIELD_REMOVE_CALLBACK_REQUEST {
    ULONG64 CallbackAddress;
} CRXSHIELD_REMOVE_CALLBACK_REQUEST, * PCRXSHIELD_REMOVE_CALLBACK_REQUEST;
#pragma pack(pop)

// Undocumented APIs - Function Pointer Definitions
typedef NTSTATUS(NTAPI* PFN_MmCopyVirtualMemory)(
    PEPROCESS SourceProcess,
    PVOID SourceAddress,
    PEPROCESS TargetProcess,
    PVOID TargetAddress,
    SIZE_T BufferSize,
    KPROCESSOR_MODE PreviousMode,
    PSIZE_T ReturnSize
);

typedef PVOID(NTAPI* PFN_PsGetProcessSectionBaseAddress)(
    PEPROCESS Process
);

// Globals
PDEVICE_OBJECT g_DeviceObject = NULL;
PDRIVER_OBJECT g_DriverObject = NULL;
PFN_MmCopyVirtualMemory g_MmCopyVirtualMemory = NULL;
PFN_PsGetProcessSectionBaseAddress g_PsGetProcessSectionBaseAddress = NULL;

// Forward declarations with correct macros
DRIVER_INITIALIZE DriverEntry;
DRIVER_UNLOAD DriverUnload;

_Dispatch_type_(IRP_MJ_CREATE)
_Dispatch_type_(IRP_MJ_CLOSE)
DRIVER_DISPATCH CreateClose;

_Dispatch_type_(IRP_MJ_DEVICE_CONTROL)
DRIVER_DISPATCH DeviceControl;

// Functions
VOID HideDriver();
VOID UnhideDriver();
NTSTATUS EnumObCallbacks(PCRXSHIELD_CALLBACK_ENTRY Entries, PULONG EntryCount, ULONG MaxEntries);
NTSTATUS RemoveObCallback(ULONG64 CallbackAddress);
PLIST_ENTRY ResolveObCallbackListHead();

// Helper: XOR deobfuscate
VOID DeobfuscateString(char* str) {
    OBFUSCATE_STRING(str);
}

// ANSI → UNICODE helper
NTSTATUS AnsiToUnicode(const char* ansiStr, PUNICODE_STRING uniStr) {
    ANSI_STRING ansi;
    RtlInitAnsiString(&ansi, ansiStr);
    return RtlAnsiStringToUnicodeString(uniStr, &ansi, TRUE);
}

// Stub for resolving ObpCallbackListHead (implement signature scan for real use)
PLIST_ENTRY ResolveObCallbackListHead() {
    // TODO: Implement proper pattern/signature scan based on your Windows version
    return NULL;
}

// Driver hiding via DKOM - DISABLED for now to prevent BSOD on unload
// Re-linking the driver properly on unload is complex and error-prone
// Enable this only if you don't need to unload the driver cleanly
static BOOLEAN g_DriverHidden = FALSE;
static LIST_ENTRY g_SavedFlink = { 0 };
static LIST_ENTRY g_SavedBlink = { 0 };
static LIST_ENTRY g_SavedHashFlink = { 0 };
static LIST_ENTRY g_SavedHashBlink = { 0 };
static USHORT g_SavedDllNameLength = 0;

VOID HideDriver() {
    // DISABLED: DKOM hiding causes BSOD on unload
    // Uncomment below for production use where driver won't be unloaded
#if 0
    if (!g_DriverObject) return;

    UNICODE_STRING name = RTL_CONSTANT_STRING(L"PsLoadedModuleList");
    PLIST_ENTRY head = (PLIST_ENTRY)MmGetSystemRoutineAddress(&name);
    if (!head) return;

    PLDR_DATA_TABLE_ENTRY entry = (PLDR_DATA_TABLE_ENTRY)g_DriverObject->DriverSection;
    if (!entry) return;

    // Save original links for unhiding
    g_SavedFlink = *entry->InLoadOrderLinks.Flink;
    g_SavedBlink = *entry->InLoadOrderLinks.Blink;
    g_SavedHashFlink = entry->HashLinks;
    g_SavedDllNameLength = entry->BaseDllName.Length;

    entry->InLoadOrderLinks.Flink->Blink = entry->InLoadOrderLinks.Blink;
    entry->InLoadOrderLinks.Blink->Flink = entry->InLoadOrderLinks.Flink;

    entry->HashLinks.Flink = entry->HashLinks.Blink = NULL;
    entry->BaseDllName.Length = 0;

    if (g_DeviceObject) {
        g_DeviceObject->NextDevice = NULL;
    }

    g_DriverHidden = TRUE;
#endif
    LOG("HideDriver: DKOM hiding disabled for safe unload");
}

VOID UnhideDriver() {
    // Nothing to do if hiding is disabled
    LOG("UnhideDriver: Nothing to restore (hiding disabled)");
}

// IRP handlers
NTSTATUS CreateClose(PDEVICE_OBJECT DeviceObject, PIRP Irp) {
    UNREFERENCED_PARAMETER(DeviceObject);
    Irp->IoStatus.Status = STATUS_SUCCESS;
    Irp->IoStatus.Information = 0;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return STATUS_SUCCESS;
}

NTSTATUS ReadProcessMemory(ULONG ProcessId, PVOID SourceAddress, PVOID Buffer, SIZE_T Size, PSIZE_T BytesRead) {
    if (!g_MmCopyVirtualMemory) return STATUS_NOT_IMPLEMENTED;

    PEPROCESS target;
    NTSTATUS status = PsLookupProcessByProcessId((HANDLE)(ULONG_PTR)ProcessId, &target);
    if (!NT_SUCCESS(status)) return status;

    __try {
        ProbeForRead(SourceAddress, Size, 1);
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        ObDereferenceObject(target);
        return STATUS_ACCESS_VIOLATION;
    }

    status = g_MmCopyVirtualMemory(target, SourceAddress, PsGetCurrentProcess(), Buffer, Size, KernelMode, BytesRead);
    ObDereferenceObject(target);
    return status;
}

NTSTATUS WriteProcessMemory(ULONG ProcessId, PVOID TargetAddress, PVOID Buffer, SIZE_T Size, PSIZE_T BytesWritten) {
    if (!g_MmCopyVirtualMemory) return STATUS_NOT_IMPLEMENTED;

    PEPROCESS target;
    NTSTATUS status = PsLookupProcessByProcessId((HANDLE)(ULONG_PTR)ProcessId, &target);
    if (!NT_SUCCESS(status)) {
        LOG("WriteProcessMemory: PsLookupProcessByProcessId failed with 0x%X", status);
        return status;
    }

    // First try normal MmCopyVirtualMemory - works for writable pages
    status = g_MmCopyVirtualMemory(PsGetCurrentProcess(), Buffer, target, TargetAddress, Size, KernelMode, BytesWritten);

    if (NT_SUCCESS(status)) {
        LOG("WriteProcessMemory: MmCopyVirtualMemory succeeded, wrote %llu bytes", (ULONG64)*BytesWritten);
        ObDereferenceObject(target);
        return status;
    }

    LOG("WriteProcessMemory: MmCopyVirtualMemory failed with 0x%X, trying MDL approach", status);

    // MmCopyVirtualMemory failed - likely a protected page (code section)
    // Use MDL to bypass page protection
    KAPC_STATE apcState;
    KeStackAttachProcess(target, &apcState);

    __try {
        // Create an MDL for the target address
        PMDL mdl = IoAllocateMdl(TargetAddress, (ULONG)Size, FALSE, FALSE, NULL);
        if (!mdl) {
            LOG("WriteProcessMemory: IoAllocateMdl failed");
            KeUnstackDetachProcess(&apcState);
            ObDereferenceObject(target);
            return STATUS_INSUFFICIENT_RESOURCES;
        }

        __try {
            // Lock the pages - this may fail if address is invalid
            MmProbeAndLockPages(mdl, KernelMode, IoReadAccess);
        }
        __except(EXCEPTION_EXECUTE_HANDLER) {
            LOG("WriteProcessMemory: MmProbeAndLockPages failed");
            IoFreeMdl(mdl);
            KeUnstackDetachProcess(&apcState);
            ObDereferenceObject(target);
            return STATUS_ACCESS_VIOLATION;
        }

        // Map the pages as writable in system space
        PVOID mapped = MmMapLockedPagesSpecifyCache(mdl, KernelMode, MmNonCached, NULL, FALSE, NormalPagePriority);
        if (!mapped) {
            LOG("WriteProcessMemory: MmMapLockedPagesSpecifyCache failed");
            MmUnlockPages(mdl);
            IoFreeMdl(mdl);
            KeUnstackDetachProcess(&apcState);
            ObDereferenceObject(target);
            return STATUS_INSUFFICIENT_RESOURCES;
        }

        // Change protection to allow writes
        status = MmProtectMdlSystemAddress(mdl, PAGE_READWRITE);
        if (!NT_SUCCESS(status)) {
            LOG("WriteProcessMemory: MmProtectMdlSystemAddress failed with 0x%X", status);
            MmUnmapLockedPages(mapped, mdl);
            MmUnlockPages(mdl);
            IoFreeMdl(mdl);
            KeUnstackDetachProcess(&apcState);
            ObDereferenceObject(target);
            return status;
        }

        // Copy the data
        RtlCopyMemory(mapped, Buffer, Size);
        *BytesWritten = Size;
        LOG("WriteProcessMemory: MDL write succeeded, wrote %llu bytes", (ULONG64)Size);

        // Cleanup
        MmUnmapLockedPages(mapped, mdl);
        MmUnlockPages(mdl);
        IoFreeMdl(mdl);
        status = STATUS_SUCCESS;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        LOG("WriteProcessMemory: Exception during MDL write");
        status = GetExceptionCode();
    }

    KeUnstackDetachProcess(&apcState);
    ObDereferenceObject(target);
    return status;
}

NTSTATUS GetProcessBaseAddress(ULONG ProcessId, PULONG64 BaseAddress) {
    if (!g_PsGetProcessSectionBaseAddress) return STATUS_NOT_IMPLEMENTED;

    PEPROCESS target;
    NTSTATUS status = PsLookupProcessByProcessId((HANDLE)(ULONG_PTR)ProcessId, &target);
    if (!NT_SUCCESS(status)) return status;

    *BaseAddress = (ULONG64)g_PsGetProcessSectionBaseAddress(target);
    ObDereferenceObject(target);
    return STATUS_SUCCESS;
}

NTSTATUS EnumObCallbacks(PCRXSHIELD_CALLBACK_ENTRY Entries, PULONG EntryCount, ULONG MaxEntries) {
    PLIST_ENTRY head = ResolveObCallbackListHead();
    if (!head) return STATUS_NOT_FOUND;

    PLIST_ENTRY cur = head->Flink;
    ULONG count = 0;

    while (cur != head && count < MaxEntries) {
        POB_CALLBACK_ENTRY e = CONTAINING_RECORD(cur, OB_CALLBACK_ENTRY, Entry);
        Entries[count].CallbackAddress = (ULONG64)e->Callback;
        Entries[count].Context = (ULONG64)e->Context;
        count++;
        cur = cur->Flink;
    }

    *EntryCount = count;
    return STATUS_SUCCESS;
}

NTSTATUS RemoveObCallback(ULONG64 CallbackAddress) {
    PLIST_ENTRY head = ResolveObCallbackListHead();
    if (!head) return STATUS_NOT_FOUND;

    PLIST_ENTRY cur = head->Flink;
    while (cur != head) {
        POB_CALLBACK_ENTRY e = CONTAINING_RECORD(cur, OB_CALLBACK_ENTRY, Entry);
        if ((ULONG64)e->Callback == CallbackAddress) {
            cur->Flink->Blink = cur->Blink;
            cur->Blink->Flink = cur->Flink;
            return STATUS_SUCCESS;
        }
        cur = cur->Flink;
    }
    return STATUS_NOT_FOUND;
}

NTSTATUS DeviceControl(PDEVICE_OBJECT DeviceObject, PIRP Irp) {
    UNREFERENCED_PARAMETER(DeviceObject);
    PIO_STACK_LOCATION stack = IoGetCurrentIrpStackLocation(Irp);
    NTSTATUS status = STATUS_SUCCESS;
    ULONG_PTR info = 0;
    PVOID buf = Irp->AssociatedIrp.SystemBuffer;
    ULONG inLen = stack->Parameters.DeviceIoControl.InputBufferLength;
    ULONG outLen = stack->Parameters.DeviceIoControl.OutputBufferLength;

    if (!buf || inLen == 0) {
        status = STATUS_INVALID_PARAMETER;
        goto complete;
    }

    switch (stack->Parameters.DeviceIoControl.IoControlCode) {
    case IOCTL_CRXSHIELD_GET_VERSION: {
        LOG("IOCTL_GET_VERSION");
        if (outLen < sizeof(CRXSHIELD_VERSION)) { status = STATUS_BUFFER_TOO_SMALL; break; }
        PCRXSHIELD_VERSION v = (PCRXSHIELD_VERSION)buf;
        v->Major = CRXSHIELD_VERSION_MAJOR;
        v->Minor = CRXSHIELD_VERSION_MINOR;
        v->Build = CRXSHIELD_VERSION_BUILD;
        info = sizeof(CRXSHIELD_VERSION);
        break;
    }
    case IOCTL_CRXSHIELD_READ_MEMORY: {
        if (inLen < sizeof(CRXSHIELD_READ_REQUEST)) { status = STATUS_BUFFER_TOO_SMALL; break; }
        PCRXSHIELD_READ_REQUEST req = (PCRXSHIELD_READ_REQUEST)buf;
        LOG("IOCTL_READ_MEMORY: PID: %d, Addr: 0x%llX, Size: %d", req->ProcessId, req->Address, req->Size);
        if (outLen < req->Size) { status = STATUS_BUFFER_TOO_SMALL; break; }
        SIZE_T read = 0;
        status = ReadProcessMemory(req->ProcessId, (PVOID)req->Address, buf, req->Size, &read);
        info = read;
        break;
    }
    case IOCTL_CRXSHIELD_WRITE_MEMORY: {
        if (inLen < sizeof(CRXSHIELD_WRITE_REQUEST) + ((PCRXSHIELD_WRITE_REQUEST)buf)->Size) { status = STATUS_BUFFER_TOO_SMALL; break; }
        PCRXSHIELD_WRITE_REQUEST req = (PCRXSHIELD_WRITE_REQUEST)buf;
        LOG("IOCTL_WRITE_MEMORY: PID: %d, Addr: 0x%llX, Size: %d", req->ProcessId, req->Address, req->Size);
        PVOID data = (PUCHAR)buf + sizeof(CRXSHIELD_WRITE_REQUEST);
        SIZE_T written = 0;
        status = WriteProcessMemory(req->ProcessId, (PVOID)req->Address, data, req->Size, &written);
        info = written;
        break;
    }
    case IOCTL_CRXSHIELD_GET_PROCESS_BASE: {
        if (inLen < sizeof(CRXSHIELD_PROCESS_BASE_REQUEST) || outLen < sizeof(CRXSHIELD_PROCESS_BASE_REQUEST)) { status = STATUS_BUFFER_TOO_SMALL; break; }
        PCRXSHIELD_PROCESS_BASE_REQUEST req = (PCRXSHIELD_PROCESS_BASE_REQUEST)buf;
        LOG("IOCTL_GET_PROCESS_BASE: PID: %d", req->ProcessId);
        ULONG64 baseAddr = 0;
        status = GetProcessBaseAddress(req->ProcessId, &baseAddr);
        req->BaseAddress = baseAddr;
        info = sizeof(CRXSHIELD_PROCESS_BASE_REQUEST);
        break;
    }
    case IOCTL_CRXSHIELD_ENUM_CALLBACKS: {
        if (inLen < sizeof(CRXSHIELD_ENUM_CALLBACKS_REQUEST)) { status = STATUS_BUFFER_TOO_SMALL; break; }
        PCRXSHIELD_ENUM_CALLBACKS_REQUEST req = (PCRXSHIELD_ENUM_CALLBACKS_REQUEST)buf;
        LOG("IOCTL_ENUM_CALLBACKS: MaxEntries: %d", req->MaxEntries);
        PVOID entries = (PUCHAR)buf + sizeof(CRXSHIELD_ENUM_CALLBACKS_REQUEST);
        ULONG count = 0;
        status = EnumObCallbacks((PCRXSHIELD_CALLBACK_ENTRY)entries, &count, req->MaxEntries);
        req->EntryCount = count;
        info = sizeof(CRXSHIELD_ENUM_CALLBACKS_REQUEST) + count * sizeof(CRXSHIELD_CALLBACK_ENTRY);
        break;
    }
    case IOCTL_CRXSHIELD_REMOVE_CALLBACK: {
        if (inLen < sizeof(CRXSHIELD_REMOVE_CALLBACK_REQUEST)) { status = STATUS_BUFFER_TOO_SMALL; break; }
        PCRXSHIELD_REMOVE_CALLBACK_REQUEST req = (PCRXSHIELD_REMOVE_CALLBACK_REQUEST)buf;
        LOG("IOCTL_REMOVE_CALLBACK: Addr: 0x%llX", req->CallbackAddress);
        status = RemoveObCallback(req->CallbackAddress);
        break;
    }
    default:
        status = STATUS_INVALID_DEVICE_REQUEST;
    }

complete:
    Irp->IoStatus.Status = status;
    Irp->IoStatus.Information = info;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return status;
}

VOID DriverUnload(PDRIVER_OBJECT DriverObject) {
    LOG("Driver Unloaded");
    UNREFERENCED_PARAMETER(DriverObject);
    char symObf[sizeof(g_ObfSymLinkName)];
    RtlCopyMemory(symObf, g_ObfSymLinkName, sizeof(g_ObfSymLinkName));
    DeobfuscateString(symObf);

    UNICODE_STRING symLink;
    if (NT_SUCCESS(AnsiToUnicode(symObf, &symLink))) {
        IoDeleteSymbolicLink(&symLink);
        RtlFreeUnicodeString(&symLink);
    }

    UnhideDriver();

    if (g_DeviceObject) {
        IoDeleteDevice(g_DeviceObject);
    }
}

NTSTATUS DriverEntry(PDRIVER_OBJECT DriverObject, PUNICODE_STRING RegistryPath) {
    UNREFERENCED_PARAMETER(RegistryPath);
    NTSTATUS status;

    // Resolve APIs
    UNICODE_STRING routineName;
    RtlInitUnicodeString(&routineName, L"MmCopyVirtualMemory");
    g_MmCopyVirtualMemory = (PFN_MmCopyVirtualMemory)MmGetSystemRoutineAddress(&routineName);

    RtlInitUnicodeString(&routineName, L"PsGetProcessSectionBaseAddress");
    g_PsGetProcessSectionBaseAddress = (PFN_PsGetProcessSectionBaseAddress)MmGetSystemRoutineAddress(&routineName);

    char devObf[sizeof(g_ObfDeviceName)];
    char symObf[sizeof(g_ObfSymLinkName)];
    RtlCopyMemory(devObf, g_ObfDeviceName, sizeof(g_ObfDeviceName));
    RtlCopyMemory(symObf, g_ObfSymLinkName, sizeof(g_ObfSymLinkName));
    DeobfuscateString(devObf);
    DeobfuscateString(symObf);

    UNICODE_STRING devName, symLink;
    status = AnsiToUnicode(devObf, &devName);
    if (!NT_SUCCESS(status)) return status;
    status = AnsiToUnicode(symObf, &symLink);
    if (!NT_SUCCESS(status)) {
        RtlFreeUnicodeString(&devName);
        return status;
    }

    status = IoCreateDevice(DriverObject, 0, &devName, FILE_DEVICE_UNKNOWN, FILE_DEVICE_SECURE_OPEN, FALSE, &g_DeviceObject);
    if (!NT_SUCCESS(status)) {
        RtlFreeUnicodeString(&devName);
        RtlFreeUnicodeString(&symLink);
        return status;
    }

    status = IoCreateSymbolicLink(&symLink, &devName);
    if (!NT_SUCCESS(status)) {
        IoDeleteDevice(g_DeviceObject);
        RtlFreeUnicodeString(&devName);
        RtlFreeUnicodeString(&symLink);
        return status;
    }

    RtlFreeUnicodeString(&devName);
    RtlFreeUnicodeString(&symLink);

    g_DriverObject = DriverObject;
    DriverObject->DriverUnload = DriverUnload;
    DriverObject->MajorFunction[IRP_MJ_CREATE] = CreateClose;
    DriverObject->MajorFunction[IRP_MJ_CLOSE] = CreateClose;
    DriverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL] = DeviceControl;

    g_DeviceObject->Flags |= DO_BUFFERED_IO;
    g_DeviceObject->Flags &= ~DO_DEVICE_INITIALIZING;

    HideDriver();
    LOG("Driver Loaded");

    return STATUS_SUCCESS;
}