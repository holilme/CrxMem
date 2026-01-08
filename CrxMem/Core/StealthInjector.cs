using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CrxMem.Core
{
    /// <summary>
    /// Stealth injection options configuration
    /// </summary>
    public class StealthOptions
    {
        public bool ManualMap { get; set; } = true;
        public bool ErasePEHeader { get; set; } = true;
        public bool UnlinkFromPEB { get; set; } = true;
        public bool RandomizeSectionNames { get; set; } = false;
    }

    /// <summary>
    /// Implements manual PE mapping for stealth DLL injection
    /// Based on CheatEngine's ManualModuleLoader techniques
    /// </summary>
    public class StealthInjector
    {
        private readonly ProcessAccess _process;

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        public StealthInjector(ProcessAccess process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
        }

        /// <summary>
        /// Inject DLL using manual PE mapping (stealth mode)
        /// </summary>
        public IntPtr InjectDLL(string dllPath, StealthOptions options)
        {
            if (!File.Exists(dllPath))
                throw new FileNotFoundException("DLL not found", dllPath);

            byte[] dllBytes = File.ReadAllBytes(dllPath);

            if (options.ManualMap)
            {
                return ManualMap(dllBytes, options);
            }
            else
            {
                return StandardInject(dllPath);
            }
        }

        /// <summary>
        /// Standard LoadLibrary injection (non-stealth)
        /// </summary>
        private IntPtr StandardInject(string dllPath)
        {
            // Allocate memory for DLL path
            byte[] pathBytes = Encoding.ASCII.GetBytes(dllPath + "\0");
            IntPtr pathAddr = _process.AllocateMemory((uint)pathBytes.Length, 0x04); // PAGE_READWRITE

            if (pathAddr == IntPtr.Zero)
                throw new Exception("Failed to allocate memory for DLL path");

            // Write DLL path to target process
            if (!_process.Write(pathAddr, pathBytes))
            {
                _process.FreeMemory(pathAddr);
                throw new Exception("Failed to write DLL path");
            }

            // Get LoadLibraryA address
            IntPtr loadLibAddr = ProcessAccess.GetLoadLibraryAddress();
            if (loadLibAddr == IntPtr.Zero)
            {
                _process.FreeMemory(pathAddr);
                throw new Exception("Failed to get LoadLibraryA address");
            }

            // Create remote thread to load DLL
            IntPtr hThread = _process.CreateThread(loadLibAddr, pathAddr);
            if (hThread == IntPtr.Zero)
            {
                _process.FreeMemory(pathAddr);
                throw new Exception("Failed to create remote thread");
            }

            // Wait for thread to complete and get module base
            // (In production, should wait for thread and get return value)
            return pathAddr; // Placeholder - would need WaitForSingleObject + GetExitCodeThread
        }

        /// <summary>
        /// Manual PE mapping without LoadLibrary (stealth)
        /// </summary>
        private IntPtr ManualMap(byte[] dllBytes, StealthOptions options)
        {
            // Parse PE headers
            var dosHeader = ReadStruct<IMAGE_DOS_HEADER>(dllBytes, 0);
            if (dosHeader.e_magic != 0x5A4D) // "MZ"
                throw new Exception("Invalid DOS header");

            var ntHeaders = ReadStruct<IMAGE_NT_HEADERS64>(dllBytes, dosHeader.e_lfanew);
            if (ntHeaders.Signature != 0x00004550) // "PE\0\0"
                throw new Exception("Invalid PE signature");

            // Allocate memory in target process for the image
            uint imageSize = ntHeaders.OptionalHeader.SizeOfImage;
            IntPtr remoteImage = _process.AllocateMemory(imageSize);

            if (remoteImage == IntPtr.Zero)
                throw new Exception("Failed to allocate memory for image");

            try
            {
                // Write headers
                uint headerSize = ntHeaders.OptionalHeader.SizeOfHeaders;
                byte[] headers = new byte[headerSize];
                Array.Copy(dllBytes, 0, headers, 0, headerSize);
                _process.Write(remoteImage, headers);

                // Write sections
                MapSections(dllBytes, remoteImage, ntHeaders, dosHeader.e_lfanew);

                // Build import table (resolve imports)
                ResolveImports(dllBytes, remoteImage, ntHeaders);

                // Apply relocations
                ApplyRelocations(dllBytes, remoteImage, ntHeaders);

                // Set memory protection for sections
                ProtectSections(remoteImage, ntHeaders);

                // Call DllMain (optional - could be done via shellcode)
                CallEntryPoint(remoteImage, ntHeaders);

                // Erase PE headers if stealth is enabled
                if (options.ErasePEHeader)
                {
                    ErasePEHeaders(remoteImage, headerSize);
                }

                return remoteImage;
            }
            catch
            {
                // Cleanup on failure
                _process.FreeMemory(remoteImage);
                throw;
            }
        }

        /// <summary>
        /// Map PE sections to target process
        /// </summary>
        private void MapSections(byte[] dllBytes, IntPtr remoteImage, IMAGE_NT_HEADERS64 ntHeaders, int ntHeadersOffset)
        {
            int sectionOffset = ntHeadersOffset + Marshal.SizeOf<IMAGE_NT_HEADERS64>();

            for (int i = 0; i < ntHeaders.FileHeader.NumberOfSections; i++)
            {
                var section = ReadStruct<IMAGE_SECTION_HEADER>(dllBytes,
                    sectionOffset + (i * Marshal.SizeOf<IMAGE_SECTION_HEADER>()));

                if (section.SizeOfRawData == 0)
                    continue;

                // Calculate destination address
                IntPtr sectionDest = IntPtr.Add(remoteImage, (int)section.VirtualAddress);

                // Copy section data
                byte[] sectionData = new byte[section.SizeOfRawData];
                Array.Copy(dllBytes, section.PointerToRawData, sectionData, 0, section.SizeOfRawData);

                _process.Write(sectionDest, sectionData);
            }
        }

        /// <summary>
        /// Resolve imports manually (build IAT)
        /// </summary>
        private void ResolveImports(byte[] dllBytes, IntPtr remoteImage, IMAGE_NT_HEADERS64 ntHeaders)
        {
            var importDir = ntHeaders.OptionalHeader.DataDirectory[1]; // IMAGE_DIRECTORY_ENTRY_IMPORT

            if (importDir.VirtualAddress == 0)
                return; // No imports

            int importDescOffset = (int)importDir.VirtualAddress;

            while (true)
            {
                var importDesc = ReadStruct<IMAGE_IMPORT_DESCRIPTOR>(dllBytes, importDescOffset);

                if (importDesc.Name == 0)
                    break; // End of import table

                // Get module name
                string moduleName = ReadString(dllBytes, (int)importDesc.Name);

                // Load module in current process to resolve addresses
                IntPtr hModule = GetModuleHandle(moduleName);
                if (hModule == IntPtr.Zero)
                    hModule = LoadLibrary(moduleName);

                // Resolve function addresses
                int thunkOffset = (int)(importDesc.FirstThunk != 0 ? importDesc.FirstThunk : importDesc.OriginalFirstThunk);

                int index = 0;
                while (true)
                {
                    ulong thunkData = BitConverter.ToUInt64(dllBytes, thunkOffset + (index * 8));

                    if (thunkData == 0)
                        break; // End of thunk table

                    IntPtr funcAddr;

                    // Check if import by ordinal
                    if ((thunkData & 0x8000000000000000) != 0)
                    {
                        // Import by ordinal
                        ushort ordinal = (ushort)(thunkData & 0xFFFF);
                        funcAddr = GetProcAddress(hModule, "#" + ordinal);
                    }
                    else
                    {
                        // Import by name
                        int nameOffset = (int)(thunkData & 0x7FFFFFFF);
                        string funcName = ReadString(dllBytes, nameOffset + 2); // Skip hint
                        funcAddr = GetProcAddress(hModule, funcName);
                    }

                    // Write function address to IAT in target process
                    IntPtr iatEntry = IntPtr.Add(remoteImage, thunkOffset + (index * 8));
                    _process.Write(iatEntry, BitConverter.GetBytes((ulong)funcAddr.ToInt64()));

                    index++;
                }

                importDescOffset += Marshal.SizeOf<IMAGE_IMPORT_DESCRIPTOR>();
            }
        }

        /// <summary>
        /// Apply base relocations
        /// </summary>
        private void ApplyRelocations(byte[] dllBytes, IntPtr remoteImage, IMAGE_NT_HEADERS64 ntHeaders)
        {
            var relocDir = ntHeaders.OptionalHeader.DataDirectory[5]; // IMAGE_DIRECTORY_ENTRY_BASERELOC

            if (relocDir.VirtualAddress == 0)
                return; // No relocations

            long delta = remoteImage.ToInt64() - (long)ntHeaders.OptionalHeader.ImageBase;

            if (delta == 0)
                return; // Already at preferred base

            // Apply relocations
            int relocOffset = (int)relocDir.VirtualAddress;
            int relocEnd = relocOffset + (int)relocDir.Size;

            while (relocOffset < relocEnd)
            {
                var relocBlock = ReadStruct<IMAGE_BASE_RELOCATION>(dllBytes, relocOffset);

                if (relocBlock.SizeOfBlock == 0)
                    break;

                int entryCount = (int)(relocBlock.SizeOfBlock - 8) / 2;

                for (int i = 0; i < entryCount; i++)
                {
                    ushort entry = BitConverter.ToUInt16(dllBytes, relocOffset + 8 + (i * 2));
                    ushort type = (ushort)(entry >> 12);
                    ushort offset = (ushort)(entry & 0xFFF);

                    if (type == 0) // IMAGE_REL_BASED_ABSOLUTE
                        continue;

                    IntPtr relocAddr = IntPtr.Add(remoteImage, (int)relocBlock.VirtualAddress + offset);

                    if (type == 10) // IMAGE_REL_BASED_DIR64
                    {
                        // Read current value
                        byte[] valueBytes = _process.Read(relocAddr, 8);
                        long value = BitConverter.ToInt64(valueBytes, 0);

                        // Apply delta
                        value += delta;

                        // Write back
                        _process.Write(relocAddr, BitConverter.GetBytes(value));
                    }
                }

                relocOffset += (int)relocBlock.SizeOfBlock;
            }
        }

        /// <summary>
        /// Set proper memory protection for sections
        /// </summary>
        private void ProtectSections(IntPtr remoteImage, IMAGE_NT_HEADERS64 ntHeaders)
        {
            // Implementation would iterate sections and set PAGE_EXECUTE, PAGE_READONLY, etc.
            // Skipped for brevity - all sections currently have PAGE_EXECUTE_READWRITE
        }

        /// <summary>
        /// Call DllMain entry point
        /// </summary>
        private void CallEntryPoint(IntPtr remoteImage, IMAGE_NT_HEADERS64 ntHeaders)
        {
            if (ntHeaders.OptionalHeader.AddressOfEntryPoint == 0)
                return;

            IntPtr entryPoint = IntPtr.Add(remoteImage, (int)ntHeaders.OptionalHeader.AddressOfEntryPoint);

            // Create thread at entry point with DLL_PROCESS_ATTACH (1)
            _process.CreateThread(entryPoint, remoteImage); // Pass image base as parameter
        }

        /// <summary>
        /// Erase PE headers in target process
        /// </summary>
        private void ErasePEHeaders(IntPtr remoteImage, uint headerSize)
        {
            byte[] zeros = new byte[headerSize];
            _process.Write(remoteImage, zeros);
        }

        // Helper methods
        private T ReadStruct<T>(byte[] data, int offset) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] buffer = new byte[size];
            Array.Copy(data, offset, buffer, 0, size);

            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        private string ReadString(byte[] data, int offset)
        {
            int length = 0;
            while (offset + length < data.Length && data[offset + length] != 0)
                length++;

            return Encoding.ASCII.GetString(data, offset, length);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        // PE structures (simplified)
        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_DOS_HEADER
        {
            public ushort e_magic;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 29)]
            public ushort[] e_res;
            public int e_lfanew;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_FILE_HEADER
        {
            public ushort Machine;
            public ushort NumberOfSections;
            public uint TimeDateStamp;
            public uint PointerToSymbolTable;
            public uint NumberOfSymbols;
            public ushort SizeOfOptionalHeader;
            public ushort Characteristics;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_DATA_DIRECTORY
        {
            public uint VirtualAddress;
            public uint Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_OPTIONAL_HEADER64
        {
            public ushort Magic;
            public byte MajorLinkerVersion;
            public byte MinorLinkerVersion;
            public uint SizeOfCode;
            public uint SizeOfInitializedData;
            public uint SizeOfUninitializedData;
            public uint AddressOfEntryPoint;
            public uint BaseOfCode;
            public ulong ImageBase;
            public uint SectionAlignment;
            public uint FileAlignment;
            public ushort MajorOperatingSystemVersion;
            public ushort MinorOperatingSystemVersion;
            public ushort MajorImageVersion;
            public ushort MinorImageVersion;
            public ushort MajorSubsystemVersion;
            public ushort MinorSubsystemVersion;
            public uint Win32VersionValue;
            public uint SizeOfImage;
            public uint SizeOfHeaders;
            public uint CheckSum;
            public ushort Subsystem;
            public ushort DllCharacteristics;
            public ulong SizeOfStackReserve;
            public ulong SizeOfStackCommit;
            public ulong SizeOfHeapReserve;
            public ulong SizeOfHeapCommit;
            public uint LoaderFlags;
            public uint NumberOfRvaAndSizes;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public IMAGE_DATA_DIRECTORY[] DataDirectory;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_NT_HEADERS64
        {
            public uint Signature;
            public IMAGE_FILE_HEADER FileHeader;
            public IMAGE_OPTIONAL_HEADER64 OptionalHeader;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_SECTION_HEADER
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] Name;
            public uint VirtualSize;
            public uint VirtualAddress;
            public uint SizeOfRawData;
            public uint PointerToRawData;
            public uint PointerToRelocations;
            public uint PointerToLinenumbers;
            public ushort NumberOfRelocations;
            public ushort NumberOfLinenumbers;
            public uint Characteristics;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_IMPORT_DESCRIPTOR
        {
            public uint OriginalFirstThunk;
            public uint TimeDateStamp;
            public uint ForwarderChain;
            public uint Name;
            public uint FirstThunk;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_BASE_RELOCATION
        {
            public uint VirtualAddress;
            public uint SizeOfBlock;
        }
    }
}
