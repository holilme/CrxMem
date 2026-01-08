#include "CrxDriverController.h"
#include <iostream>

CrxDriverController::CrxDriverController() : m_hDevice(INVALID_HANDLE_VALUE) {}

CrxDriverController::~CrxDriverController() {
    Disconnect();
}

bool CrxDriverController::Connect() {
    if (m_hDevice != INVALID_HANDLE_VALUE) return true;

    m_hDevice = CreateFileA(
        "\\\\.\\CrxShield",
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL
    );

    return m_hDevice != INVALID_HANDLE_VALUE;
}

void CrxDriverController::Disconnect() {
    if (m_hDevice != INVALID_HANDLE_VALUE) {
        CloseHandle(m_hDevice);
        m_hDevice = INVALID_HANDLE_VALUE;
    }
}

bool CrxDriverController::IsConnected() const {
    return m_hDevice != INVALID_HANDLE_VALUE;
}

bool CrxDriverController::GetVersion(ULONG& Major, ULONG& Minor, ULONG& Build) {
    if (!IsConnected()) return false;

    CRXSHIELD_VERSION version = { 0 };
    DWORD bytesReturned = 0;

    if (DeviceIoControl(
        m_hDevice,
        IOCTL_CRXSHIELD_GET_VERSION,
        NULL, 0,
        &version, sizeof(version),
        &bytesReturned,
        NULL
    )) {
        Major = version.Major;
        Minor = version.Minor;
        Build = version.Build;
        return true;
    }

    return false;
}

bool CrxDriverController::ReadProcessMemory(ULONG ProcessId, uint64_t Address, void* Buffer, uint32_t Size) {
    if (!IsConnected() || !Buffer || Size == 0) return false;

    CRXSHIELD_READ_REQUEST req;
    req.ProcessId = ProcessId;
    req.Address = Address;
    req.Size = Size;

    DWORD bytesReturned = 0;
    return DeviceIoControl(
        m_hDevice,
        IOCTL_CRXSHIELD_READ_MEMORY,
        &req, sizeof(req),
        Buffer, Size,
        &bytesReturned,
        NULL
    );
}

bool CrxDriverController::WriteProcessMemory(ULONG ProcessId, uint64_t Address, const void* Buffer, uint32_t Size) {
    if (!IsConnected() || !Buffer || Size == 0) return false;

    // Allocate a buffer that holds both the request struct and the data to write
    size_t totalSize = sizeof(CRXSHIELD_WRITE_REQUEST) + Size;
    std::vector<uint8_t> packet(totalSize);

    PCRXSHIELD_WRITE_REQUEST req = (PCRXSHIELD_WRITE_REQUEST)packet.data();
    req->ProcessId = ProcessId;
    req->Address = Address;
    req->Size = Size;

    // Copy data after the struct
    memcpy(packet.data() + sizeof(CRXSHIELD_WRITE_REQUEST), Buffer, Size);

    DWORD bytesReturned = 0;
    return DeviceIoControl(
        m_hDevice,
        IOCTL_CRXSHIELD_WRITE_MEMORY,
        packet.data(), (DWORD)totalSize,
        NULL, 0,
        &bytesReturned,
        NULL
    );
}

bool CrxDriverController::GetProcessBaseAddress(ULONG ProcessId, uint64_t& BaseAddress) {
    if (!IsConnected()) return false;

    CRXSHIELD_PROCESS_BASE_REQUEST req;
    req.ProcessId = ProcessId;
    req.BaseAddress = 0;

    DWORD bytesReturned = 0;
    if (DeviceIoControl(
        m_hDevice,
        IOCTL_CRXSHIELD_GET_PROCESS_BASE,
        &req, sizeof(req),
        &req, sizeof(req),
        &bytesReturned,
        NULL
    )) {
        BaseAddress = req.BaseAddress;
        return true;
    }
    return false;
}

bool CrxDriverController::EnumObCallbacks(std::vector<CRXSHIELD_CALLBACK_ENTRY>& Callbacks) {
    if (!IsConnected()) return false;

    // Start with a reasonable buffer size
    const ULONG MAX_ENTRIES = 100;
    size_t bufferSize = sizeof(CRXSHIELD_ENUM_CALLBACKS_REQUEST) + (MAX_ENTRIES * sizeof(CRXSHIELD_CALLBACK_ENTRY));
    std::vector<uint8_t> buffer(bufferSize);

    PCRXSHIELD_ENUM_CALLBACKS_REQUEST req = (PCRXSHIELD_ENUM_CALLBACKS_REQUEST)buffer.data();
    req->MaxEntries = MAX_ENTRIES;
    req->EntryCount = 0;

    DWORD bytesReturned = 0;
    if (DeviceIoControl(
        m_hDevice,
        IOCTL_CRXSHIELD_ENUM_CALLBACKS,
        buffer.data(), (DWORD)bufferSize,
        buffer.data(), (DWORD)bufferSize,
        &bytesReturned,
        NULL
    )) {
        PCRXSHIELD_CALLBACK_ENTRY entries = (PCRXSHIELD_CALLBACK_ENTRY)(buffer.data() + sizeof(CRXSHIELD_ENUM_CALLBACKS_REQUEST));
        for (ULONG i = 0; i < req->EntryCount; i++) {
            Callbacks.push_back(entries[i]);
        }
        return true;
    }
    return false;
}

bool CrxDriverController::RemoveObCallback(uint64_t CallbackAddress) {
    if (!IsConnected()) return false;

    CRXSHIELD_REMOVE_CALLBACK_REQUEST req;
    req.CallbackAddress = CallbackAddress;

    DWORD bytesReturned = 0;
    return DeviceIoControl(
        m_hDevice,
        IOCTL_CRXSHIELD_REMOVE_CALLBACK,
        &req, sizeof(req),
        NULL, 0,
        &bytesReturned,
        NULL
    );
}
