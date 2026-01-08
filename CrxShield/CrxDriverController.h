#pragma once
#include <Windows.h>
#include <cstdint>
#include <vector>
#include <string>

// IOCTL Definitions
#define IOCTL_BASE 0x1337
#define IOCTL_CRXSHIELD_GET_VERSION     CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x0, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_CRXSHIELD_READ_MEMORY     CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x1, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_CRXSHIELD_WRITE_MEMORY    CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x2, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_CRXSHIELD_GET_PROCESS_BASE CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x3, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_CRXSHIELD_ENUM_CALLBACKS  CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x4, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_CRXSHIELD_REMOVE_CALLBACK CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x5, METHOD_BUFFERED, FILE_ANY_ACCESS)

// Shared Structures
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

class CrxDriverController {
public:
    CrxDriverController();
    ~CrxDriverController();

    // Connection management
    bool Connect();
    void Disconnect();
    bool IsConnected() const;

    // Driver Operations
    bool GetVersion(ULONG& Major, ULONG& Minor, ULONG& Build);
    bool ReadProcessMemory(ULONG ProcessId, uint64_t Address, void* Buffer, uint32_t Size);
    bool WriteProcessMemory(ULONG ProcessId, uint64_t Address, const void* Buffer, uint32_t Size);
    bool GetProcessBaseAddress(ULONG ProcessId, uint64_t& BaseAddress);
    
    // Advanced Features
    bool EnumObCallbacks(std::vector<CRXSHIELD_CALLBACK_ENTRY>& Callbacks);
    bool RemoveObCallback(uint64_t CallbackAddress);

private:
    HANDLE m_hDevice;
};
