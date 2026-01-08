using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace CrxMem.Core
{
    /// <summary>
    /// Shared memory structure for VEH communication (matches VEHDebug.h)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VEHSharedMemStruct
    {
        // Magic number for validation - MUST be first field
        public int Magic;               // Should be VEH_SHARED_MEM_MAGIC (0x56454844) when valid

        // Control flags
        public int Active;              // 1 = monitoring active, 0 = stop
        public int HitCount;            // Total number of hits

        // Breakpoint configuration
        public ulong WatchAddress;      // Address being monitored
        public int BreakpointSize;      // 1, 2, 4, or 8
        public int BreakpointType;      // 1 = Write only, 3 = Read/Write
        public int BreakpointSlot;      // Which DR register (0-3)

        // Hit buffer indices
        public int WriteIndex;          // Next write position
        public int ReadIndex;           // Next read position

        // PAGE_GUARD mode fields
        public int UsePageGuard;        // 1 = PAGE_GUARD mode, 0 = HW BP mode
        public int NeedReapplyGuard;    // (Deprecated - DLL handles via single-step)
        public ulong PageBase;          // Base address of watched page (4KB aligned)
        public int PageSize;            // Size of page (usually 4096)
        public uint OriginalProtection; // Original page protection before PAGE_GUARD
    }

    /// <summary>
    /// Manages shared memory for VEH-based breakpoint monitoring
    /// </summary>
    public class VEHSharedMemory : IDisposable
    {
        public const int MAX_HITS = 1024;
        // Magic number to validate shared memory is still valid
        private const int VEH_SHARED_MEM_MAGIC = 0x56454844;  // "VEHD" in little-endian

        // C++ struct layout (pack=1):
        // LONG Magic (4) + LONG Active (4) + LONG HitCount (4) + DWORD64 WatchAddress (8) +
        // DWORD BreakpointSize (4) + DWORD BreakpointType (4) + DWORD BreakpointSlot (4) +
        // LONG WriteIndex (4) + LONG ReadIndex (4) +
        // LONG UsePageGuard (4) + LONG NeedReapplyGuard (4) + DWORD64 PageBase (8) + DWORD PageSize (4) +
        // DWORD OriginalProtection (4) + LONG ActiveHandlers (4) + LONG ShutdownRequested (4) + DWORD Reserved[2] (8)
        // = 80 bytes header (before hit arrays)
        private const int HEADER_SIZE = 80;

        // Offsets for individual fields (Magic is at 0)
        private const int MAGIC_OFFSET = 0;
        private const int ACTIVE_OFFSET = 4;
        private const int HIT_COUNT_OFFSET = 8;
        private const int WATCH_ADDRESS_OFFSET = 12;
        private const int BP_SIZE_OFFSET = 20;
        private const int BP_TYPE_OFFSET = 24;
        private const int BP_SLOT_OFFSET = 28;
        private const int WRITE_INDEX_OFFSET = 32;
        private const int READ_INDEX_OFFSET = 36;
        private const int USE_PAGE_GUARD_OFFSET = 40;
        private const int NEED_REAPPLY_GUARD_OFFSET = 44;
        private const int PAGE_BASE_OFFSET = 48;
        private const int PAGE_SIZE_OFFSET = 56;
        private const int ORIGINAL_PROTECTION_OFFSET = 60;

        // NEW: Synchronization fields for safe shutdown
        private const int ACTIVE_HANDLERS_OFFSET = 64;     // Count of VEH handlers currently executing
        private const int SHUTDOWN_REQUESTED_OFFSET = 68;  // 1 when shutdown in progress
        // Reserved[2] at offsets 72-79

        private const int HIT_ADDRESSES_OFFSET = HEADER_SIZE;
        private const int HIT_THREADS_OFFSET = HIT_ADDRESSES_OFFSET + (MAX_HITS * 8); // 8 bytes per address
        private const int TOTAL_SIZE = HIT_THREADS_OFFSET + (MAX_HITS * 4) + 8; // + 4 bytes per thread + StopEvent handle (8 bytes on x64)

        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _accessor;
        private string _name;
        private bool _disposed;

        public string Name => _name;
        public bool IsValid => _accessor != null;

        public VEHSharedMemory()
        {
            // CRITICAL: Use "Local\\" prefix for cross-process memory mapping
            // Without this prefix, the target process may fail to open the shared memory
            _name = $"Local\\CrxMem_VEH_{Guid.NewGuid():N}";
        }

        /// <summary>
        /// Create the shared memory region
        /// </summary>
        public bool Create()
        {
            try
            {
                _mmf = MemoryMappedFile.CreateNew(_name, TOTAL_SIZE, MemoryMappedFileAccess.ReadWrite);
                _accessor = _mmf.CreateViewAccessor(0, TOTAL_SIZE, MemoryMappedFileAccess.ReadWrite);

                // Initialize to zeros
                for (int i = 0; i < TOTAL_SIZE; i++)
                {
                    _accessor.Write(i, (byte)0);
                }

                // Set magic number to mark shared memory as valid
                _accessor.Write(MAGIC_OFFSET, VEH_SHARED_MEM_MAGIC);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create shared memory: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Set the monitoring as active/inactive
        /// </summary>
        public void SetActive(bool active)
        {
            _accessor?.Write(ACTIVE_OFFSET, active ? 1 : 0);
        }

        /// <summary>
        /// Check if monitoring is active
        /// </summary>
        public bool IsActive()
        {
            if (_accessor == null) return false;
            return _accessor.ReadInt32(ACTIVE_OFFSET) != 0;
        }

        /// <summary>
        /// Get total hit count
        /// </summary>
        public int GetHitCount()
        {
            if (_accessor == null) return 0;
            return _accessor.ReadInt32(HIT_COUNT_OFFSET);
        }

        /// <summary>
        /// Configure the breakpoint (hardware breakpoint mode)
        /// </summary>
        public void Configure(ulong address, int type, int size, int slot)
        {
            if (_accessor == null) return;

            _accessor.Write(WATCH_ADDRESS_OFFSET, address);
            _accessor.Write(BP_SIZE_OFFSET, size);
            _accessor.Write(BP_TYPE_OFFSET, type);
            _accessor.Write(BP_SLOT_OFFSET, slot);
        }

        /// <summary>
        /// Configure PAGE_GUARD mode
        /// </summary>
        public void ConfigurePageGuard(ulong watchAddress, ulong pageBase, int pageSize, int watchSize = 4, uint originalProtection = 0)
        {
            if (_accessor == null) return;

            _accessor.Write(WATCH_ADDRESS_OFFSET, watchAddress);
            _accessor.Write(BP_SIZE_OFFSET, watchSize);  // Important: set watch size for address filtering
            _accessor.Write(USE_PAGE_GUARD_OFFSET, 1);
            _accessor.Write(PAGE_BASE_OFFSET, pageBase);
            _accessor.Write(PAGE_SIZE_OFFSET, pageSize);
            _accessor.Write(ORIGINAL_PROTECTION_OFFSET, originalProtection);
            _accessor.Write(NEED_REAPPLY_GUARD_OFFSET, 0);
        }

        /// <summary>
        /// Set the original page protection (before PAGE_GUARD was added)
        /// </summary>
        public void SetOriginalProtection(uint protection)
        {
            _accessor?.Write(ORIGINAL_PROTECTION_OFFSET, protection);
        }

        /// <summary>
        /// Disable PAGE_GUARD mode (switch to hardware breakpoint mode)
        /// </summary>
        public void DisablePageGuard()
        {
            _accessor?.Write(USE_PAGE_GUARD_OFFSET, 0);
        }

        /// <summary>
        /// Check if we need to re-apply the PAGE_GUARD
        /// </summary>
        public bool NeedsReapplyGuard()
        {
            if (_accessor == null) return false;
            return _accessor.ReadInt32(NEED_REAPPLY_GUARD_OFFSET) != 0;
        }

        /// <summary>
        /// Clear the re-apply guard flag
        /// </summary>
        public void ClearReapplyGuard()
        {
            _accessor?.Write(NEED_REAPPLY_GUARD_OFFSET, 0);
        }

        /// <summary>
        /// Get the page base address
        /// </summary>
        public ulong GetPageBase()
        {
            if (_accessor == null) return 0;
            return _accessor.ReadUInt64(PAGE_BASE_OFFSET);
        }

        /// <summary>
        /// Get current write index
        /// </summary>
        public int GetWriteIndex()
        {
            if (_accessor == null) return 0;
            return _accessor.ReadInt32(WRITE_INDEX_OFFSET);
        }

        /// <summary>
        /// Get current read index
        /// </summary>
        public int GetReadIndex()
        {
            if (_accessor == null) return 0;
            return _accessor.ReadInt32(READ_INDEX_OFFSET);
        }

        /// <summary>
        /// Set read index
        /// </summary>
        public void SetReadIndex(int index)
        {
            _accessor?.Write(READ_INDEX_OFFSET, index);
        }

        /// <summary>
        /// Read a hit entry
        /// </summary>
        public (ulong address, uint threadId) ReadHit(int index)
        {
            if (_accessor == null) return (0, 0);

            int idx = index % MAX_HITS;
            int addressOffset = HIT_ADDRESSES_OFFSET + (idx * 8);
            ulong address = _accessor.ReadUInt64(addressOffset);
            uint threadId = _accessor.ReadUInt32(HIT_THREADS_OFFSET + (idx * 4));

            // Debug output
            System.Diagnostics.Debug.WriteLine($"ReadHit[{idx}]: offset={addressOffset}, raw=0x{address:X16}, threadId={threadId}");

            return (address, threadId);
        }

        /// <summary>
        /// Read all new hits since last read.
        /// Handles lock-free circular buffer where WriteIndex increments atomically.
        /// </summary>
        public System.Collections.Generic.List<(ulong address, uint threadId)> ReadNewHits()
        {
            var hits = new System.Collections.Generic.List<(ulong, uint)>();

            if (_accessor == null) return hits;

            // Read WriteIndex - the VEH handler increments this atomically
            // Memory-mapped I/O ensures we see the latest value
            int writeIdx = GetWriteIndex();
            int readIdx = GetReadIndex();

            // Handle the case where the buffer may have wrapped multiple times
            // Calculate how many entries are pending
            int pendingHits = writeIdx - readIdx;

            // If negative or very large, buffer wrapped - just read what's available
            if (pendingHits < 0 || pendingHits > MAX_HITS)
            {
                // Buffer overflowed, skip to most recent data
                // Start from (writeIdx - MAX_HITS) to get the oldest valid data
                readIdx = writeIdx - MAX_HITS;
                if (readIdx < 0) readIdx = 0;
            }

            // Read hits in order (limit to MAX_HITS to prevent infinite loop)
            int count = 0;
            while (readIdx < writeIdx && count < MAX_HITS)
            {
                int actualIdx = readIdx % MAX_HITS;
                var hit = ReadHit(actualIdx);
                if (hit.address != 0)
                {
                    hits.Add(hit);
                }
                readIdx++;
                count++;
            }

            SetReadIndex(readIdx);
            return hits;
        }

        /// <summary>
        /// Get count of VEH handlers currently executing
        /// </summary>
        public int GetActiveHandlers()
        {
            if (_accessor == null) return 0;
            return _accessor.ReadInt32(ACTIVE_HANDLERS_OFFSET);
        }

        /// <summary>
        /// Signal that shutdown is requested
        /// </summary>
        public void SetShutdownRequested(bool requested)
        {
            _accessor?.Write(SHUTDOWN_REQUESTED_OFFSET, requested ? 1 : 0);
        }

        /// <summary>
        /// Invalidate the magic number to signal the DLL to stop using shared memory
        /// </summary>
        public void InvalidateMagic()
        {
            _accessor?.Write(MAGIC_OFFSET, 0);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Invalidate magic number first to signal DLL the memory is no longer valid
                _accessor?.Write(MAGIC_OFFSET, 0);
                SetActive(false);

                // Small delay to let VEH handler see the invalidated magic
                System.Threading.Thread.Sleep(50);

                _accessor?.Dispose();
                _mmf?.Dispose();
                _accessor = null;
                _mmf = null;
                _disposed = true;
            }
        }
    }
}
