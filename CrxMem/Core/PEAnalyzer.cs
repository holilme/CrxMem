using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CrxMem.Core
{
    /// <summary>
    /// Comprehensive PE (Portable Executable) file analyzer
    /// Parses imports, exports, sections, and security features
    /// </summary>
    public class PEAnalyzer
    {
        public class PEInfo
        {
            public string FilePath { get; set; } = "";
            public bool IsValid { get; set; }
            public bool Is64Bit { get; set; }
            public DateTime CompilationTime { get; set; }
            public uint ImageBase { get; set; }
            public uint EntryPoint { get; set; }
            public ushort Subsystem { get; set; }
            public string SubsystemName { get; set; } = "";

            // Security features
            public bool HasASLR { get; set; }
            public bool HasDEP { get; set; }
            public bool HasCFG { get; set; }
            public bool HasSEH { get; set; }
            public bool HasAuthenticode { get; set; }
            public string SignatureInfo { get; set; } = "";

            // Advanced analysis
            public string ImpHash { get; set; } = "";
            public bool HasOverlay { get; set; }
            public int OverlaySize { get; set; }
            public bool HasTLSCallbacks { get; set; }
            public List<uint> TLSCallbackAddresses { get; set; } = new();
            public string CompilerInfo { get; set; } = "";
            public List<string> SuspiciousIndicators { get; set; } = new();
            public RichHeaderInfo? RichHeader { get; set; }

            // Import/Export deep analysis
            public bool HasDelayLoadImports { get; set; }
            public bool HasBoundImports { get; set; }
            public bool HasForwardedExports { get; set; }
            public int OrdinalOnlyImports { get; set; }
            public Dictionary<string, int> APICategories { get; set; } = new();
            public List<string> MITREPatterns { get; set; } = new();
            public DependencyAnalysis? Dependencies { get; set; }

            // Collections
            public List<ImportedDLL> Imports { get; set; } = new();
            public List<DelayLoadDLL> DelayImports { get; set; } = new();
            public List<ExportedFunction> Exports { get; set; } = new();
            public List<SectionInfo> Sections { get; set; } = new();
            public List<string> DetectedPackers { get; set; } = new();
            public List<ResourceInfo> Resources { get; set; } = new();
            public VersionInfo? FileVersion { get; set; }
        }

        public class ImportedDLL
        {
            public string DLLName { get; set; } = "";
            public string Category { get; set; } = "";
            public List<string> Functions { get; set; } = new();
            public List<ImportedFunction> DetailedFunctions { get; set; } = new();
            public bool IsBound { get; set; }
            public uint BoundTimestamp { get; set; }
        }

        public class ImportedFunction
        {
            public string Name { get; set; } = "";
            public uint Ordinal { get; set; }
            public bool IsOrdinalOnly { get; set; }
            public uint Hint { get; set; }
            public string BehaviorCategory { get; set; } = "";
            public string MITRETechnique { get; set; } = "";
        }

        public class DelayLoadDLL
        {
            public string DLLName { get; set; } = "";
            public List<string> Functions { get; set; } = new();
            public string Reason { get; set; } = "";
        }

        public class ExportedFunction
        {
            public string Name { get; set; } = "";
            public uint Ordinal { get; set; }
            public uint RVA { get; set; }
            public bool IsForwarded { get; set; }
            public string? ForwarderChain { get; set; }
        }

        public class SectionInfo
        {
            public string Name { get; set; } = "";
            public uint VirtualAddress { get; set; }
            public uint VirtualSize { get; set; }
            public uint RawSize { get; set; }
            public uint Characteristics { get; set; }
            public double Entropy { get; set; }
            public string Flags { get; set; } = "";
        }

        public class ResourceInfo
        {
            public string Type { get; set; } = "";
            public string Name { get; set; } = "";
            public int Size { get; set; }
            public string Language { get; set; } = "";
        }

        public class RichHeaderInfo
        {
            public bool IsValid { get; set; }
            public bool IsTampered { get; set; }
            public List<CompilerEntry> Compilers { get; set; } = new();
            public string Checksum { get; set; } = "";
        }

        public class CompilerEntry
        {
            public string ProductName { get; set; } = "";
            public ushort ProductId { get; set; }
            public ushort BuildNumber { get; set; }
            public int UseCount { get; set; }
        }

        public class VersionInfo
        {
            public string FileVersion { get; set; } = "";
            public string ProductVersion { get; set; } = "";
            public string CompanyName { get; set; } = "";
            public string FileDescription { get; set; } = "";
            public string InternalName { get; set; } = "";
            public string OriginalFilename { get; set; } = "";
            public string ProductName { get; set; } = "";
            public string LegalCopyright { get; set; } = "";
            public string LegalTrademarks { get; set; } = "";
        }

        public class DependencyAnalysis
        {
            public int TotalDependencies { get; set; }
            public int MaxDepth { get; set; }
            public List<string> MissingDependencies { get; set; } = new();
            public List<string> CircularDependencies { get; set; } = new();
            public Dictionary<string, int> DependencyTree { get; set; } = new();
        }

        // PE structures
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
        private struct IMAGE_OPTIONAL_HEADER32
        {
            public ushort Magic;
            public byte MajorLinkerVersion;
            public byte MinorLinkerVersion;
            public uint SizeOfCode;
            public uint SizeOfInitializedData;
            public uint SizeOfUninitializedData;
            public uint AddressOfEntryPoint;
            public uint BaseOfCode;
            public uint BaseOfData;
            public uint ImageBase;
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
            public uint SizeOfStackReserve;
            public uint SizeOfStackCommit;
            public uint SizeOfHeapReserve;
            public uint SizeOfHeapCommit;
            public uint LoaderFlags;
            public uint NumberOfRvaAndSizes;
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
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_DATA_DIRECTORY
        {
            public uint VirtualAddress;
            public uint Size;
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
        private struct IMAGE_EXPORT_DIRECTORY
        {
            public uint Characteristics;
            public uint TimeDateStamp;
            public ushort MajorVersion;
            public ushort MinorVersion;
            public uint Name;
            public uint Base;
            public uint NumberOfFunctions;
            public uint NumberOfNames;
            public uint AddressOfFunctions;
            public uint AddressOfNames;
            public uint AddressOfNameOrdinals;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_TLS_DIRECTORY32
        {
            public uint StartAddressOfRawData;
            public uint EndAddressOfRawData;
            public uint AddressOfIndex;
            public uint AddressOfCallBacks;
            public uint SizeOfZeroFill;
            public uint Characteristics;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_TLS_DIRECTORY64
        {
            public ulong StartAddressOfRawData;
            public ulong EndAddressOfRawData;
            public ulong AddressOfIndex;
            public ulong AddressOfCallBacks;
            public uint SizeOfZeroFill;
            public uint Characteristics;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_DELAYLOAD_DESCRIPTOR
        {
            public uint Attributes;
            public uint DllNameRVA;
            public uint ModuleHandleRVA;
            public uint ImportAddressTableRVA;
            public uint ImportNameTableRVA;
            public uint BoundImportAddressTableRVA;
            public uint UnloadInformationTableRVA;
            public uint TimeDateStamp;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_BOUND_IMPORT_DESCRIPTOR
        {
            public uint TimeDateStamp;
            public ushort OffsetModuleName;
            public ushort NumberOfModuleForwarderRefs;
        }

        // Constants
        private const ushort IMAGE_DOS_SIGNATURE = 0x5A4D; // MZ
        private const uint IMAGE_NT_SIGNATURE = 0x00004550; // PE00
        private const ushort IMAGE_FILE_MACHINE_I386 = 0x014c;
        private const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664;
        private const ushort IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE = 0x0040;
        private const ushort IMAGE_DLLCHARACTERISTICS_NX_COMPAT = 0x0100;
        private const ushort IMAGE_DLLCHARACTERISTICS_NO_SEH = 0x0400;
        private const ushort IMAGE_DLLCHARACTERISTICS_GUARD_CF = 0x4000;

        // Data directory indices
        private const int IMAGE_DIRECTORY_ENTRY_EXPORT = 0;
        private const int IMAGE_DIRECTORY_ENTRY_IMPORT = 1;
        private const int IMAGE_DIRECTORY_ENTRY_RESOURCE = 2;
        private const int IMAGE_DIRECTORY_ENTRY_SECURITY = 4;
        private const int IMAGE_DIRECTORY_ENTRY_BASERELOC = 5;
        private const int IMAGE_DIRECTORY_ENTRY_BOUND_IMPORT = 11;
        private const int IMAGE_DIRECTORY_ENTRY_TLS = 9;
        private const int IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT = 13;

        // Rich header signature
        private const uint RICH_SIGNATURE = 0x68636952; // "Rich"
        private const uint DANS_SIGNATURE = 0x536E6144; // "DanS"

        /// <summary>
        /// Analyze a PE file from disk
        /// </summary>
        public static PEInfo AnalyzeFile(string filePath)
        {
            var peInfo = new PEInfo { FilePath = filePath };

            try
            {
                if (!File.Exists(filePath))
                {
                    peInfo.IsValid = false;
                    return peInfo;
                }

                byte[] fileBytes = File.ReadAllBytes(filePath);
                return AnalyzeBytes(fileBytes, filePath);
            }
            catch
            {
                peInfo.IsValid = false;
                return peInfo;
            }
        }

        /// <summary>
        /// Analyze PE from memory
        /// </summary>
        public static PEInfo AnalyzeMemory(ProcessAccess process, IntPtr baseAddress)
        {
            var peInfo = new PEInfo { FilePath = $"Memory:{baseAddress:X}" };

            try
            {
                // Read DOS header
                byte[] dosHeaderBytes = process.Read(baseAddress, Marshal.SizeOf<IMAGE_DOS_HEADER>());
                IMAGE_DOS_HEADER dosHeader = BytesToStruct<IMAGE_DOS_HEADER>(dosHeaderBytes);

                if (dosHeader.e_magic != IMAGE_DOS_SIGNATURE)
                {
                    peInfo.IsValid = false;
                    return peInfo;
                }

                // Read entire PE (up to 50MB max)
                int maxSize = 50 * 1024 * 1024;
                byte[] peBytes = process.Read(baseAddress, maxSize);

                return AnalyzeBytes(peBytes, $"Memory:{baseAddress:X}");
            }
            catch
            {
                peInfo.IsValid = false;
                return peInfo;
            }
        }

        /// <summary>
        /// Analyze PE from byte array
        /// </summary>
        private static PEInfo AnalyzeBytes(byte[] data, string source)
        {
            var peInfo = new PEInfo { FilePath = source };

            try
            {
                // Parse DOS header
                IMAGE_DOS_HEADER dosHeader = BytesToStruct<IMAGE_DOS_HEADER>(data, 0);
                if (dosHeader.e_magic != IMAGE_DOS_SIGNATURE)
                {
                    peInfo.IsValid = false;
                    return peInfo;
                }

                // Parse NT headers
                uint ntHeaderOffset = (uint)dosHeader.e_lfanew;
                uint ntSignature = BitConverter.ToUInt32(data, (int)ntHeaderOffset);
                if (ntSignature != IMAGE_NT_SIGNATURE)
                {
                    peInfo.IsValid = false;
                    return peInfo;
                }

                peInfo.IsValid = true;

                // Parse file header
                IMAGE_FILE_HEADER fileHeader = BytesToStruct<IMAGE_FILE_HEADER>(data, (int)(ntHeaderOffset + 4));
                peInfo.Is64Bit = fileHeader.Machine == IMAGE_FILE_MACHINE_AMD64;
                peInfo.CompilationTime = DateTimeOffset.FromUnixTimeSeconds(fileHeader.TimeDateStamp).DateTime;

                // Parse optional header
                int optHeaderOffset = (int)(ntHeaderOffset + 4 + Marshal.SizeOf<IMAGE_FILE_HEADER>());
                ushort magic = BitConverter.ToUInt16(data, optHeaderOffset);
                peInfo.Is64Bit = (magic == 0x20b); // PE32+

                if (peInfo.Is64Bit)
                {
                    IMAGE_OPTIONAL_HEADER64 optHeader = BytesToStruct<IMAGE_OPTIONAL_HEADER64>(data, optHeaderOffset);
                    peInfo.ImageBase = (uint)optHeader.ImageBase;
                    peInfo.EntryPoint = optHeader.AddressOfEntryPoint;
                    peInfo.Subsystem = optHeader.Subsystem;
                    peInfo.HasASLR = (optHeader.DllCharacteristics & IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE) != 0;
                    peInfo.HasDEP = (optHeader.DllCharacteristics & IMAGE_DLLCHARACTERISTICS_NX_COMPAT) != 0;
                    peInfo.HasSEH = (optHeader.DllCharacteristics & IMAGE_DLLCHARACTERISTICS_NO_SEH) == 0;
                    peInfo.HasCFG = (optHeader.DllCharacteristics & IMAGE_DLLCHARACTERISTICS_GUARD_CF) != 0;

                    // Parse data directories
                    int dataDirectoryOffset = optHeaderOffset + Marshal.SizeOf<IMAGE_OPTIONAL_HEADER64>();
                    ParseDataDirectories(data, dataDirectoryOffset, (int)optHeader.NumberOfRvaAndSizes, fileHeader.NumberOfSections,
                        (int)(ntHeaderOffset + 4 + Marshal.SizeOf<IMAGE_FILE_HEADER>() + fileHeader.SizeOfOptionalHeader), peInfo);
                }
                else
                {
                    IMAGE_OPTIONAL_HEADER32 optHeader = BytesToStruct<IMAGE_OPTIONAL_HEADER32>(data, optHeaderOffset);
                    peInfo.ImageBase = optHeader.ImageBase;
                    peInfo.EntryPoint = optHeader.AddressOfEntryPoint;
                    peInfo.Subsystem = optHeader.Subsystem;
                    peInfo.HasASLR = (optHeader.DllCharacteristics & IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE) != 0;
                    peInfo.HasDEP = (optHeader.DllCharacteristics & IMAGE_DLLCHARACTERISTICS_NX_COMPAT) != 0;
                    peInfo.HasSEH = (optHeader.DllCharacteristics & IMAGE_DLLCHARACTERISTICS_NO_SEH) == 0;
                    peInfo.HasCFG = (optHeader.DllCharacteristics & IMAGE_DLLCHARACTERISTICS_GUARD_CF) != 0;

                    // Parse data directories
                    int dataDirectoryOffset = optHeaderOffset + Marshal.SizeOf<IMAGE_OPTIONAL_HEADER32>();
                    ParseDataDirectories(data, dataDirectoryOffset, (int)optHeader.NumberOfRvaAndSizes, fileHeader.NumberOfSections,
                        (int)(ntHeaderOffset + 4 + Marshal.SizeOf<IMAGE_FILE_HEADER>() + fileHeader.SizeOfOptionalHeader), peInfo);
                }

                peInfo.SubsystemName = GetSubsystemName(peInfo.Subsystem);

                // Parse sections
                int sectionHeaderOffset = (int)(ntHeaderOffset + 4 + Marshal.SizeOf<IMAGE_FILE_HEADER>() + fileHeader.SizeOfOptionalHeader);
                ParseSections(data, sectionHeaderOffset, fileHeader.NumberOfSections, peInfo);

                // Advanced analysis
                ParseRichHeader(data, dosHeader.e_lfanew, peInfo);
                DetectPackers(data, peInfo);
                DetectOverlay(data, peInfo);
                CalculateImpHash(peInfo);
                AnalyzeImportPatterns(peInfo);
                AnalyzeExportForwarding(data, fileHeader.NumberOfSections, sectionHeaderOffset, peInfo);
                BuildDependencyTree(peInfo);
                DetectSuspiciousIndicators(peInfo);
                CheckAuthenticode(data, peInfo);

                return peInfo;
            }
            catch
            {
                peInfo.IsValid = false;
                return peInfo;
            }
        }

        private static void ParseDataDirectories(byte[] data, int offset, int count, int sectionCount, int sectionHeaderOffset, PEInfo peInfo)
        {
            try
            {
                int directorySize = Marshal.SizeOf<IMAGE_DATA_DIRECTORY>();

                // Export directory (index 0)
                if (count > IMAGE_DIRECTORY_ENTRY_EXPORT)
                {
                    IMAGE_DATA_DIRECTORY exportDir = BytesToStruct<IMAGE_DATA_DIRECTORY>(data, offset + (IMAGE_DIRECTORY_ENTRY_EXPORT * directorySize));
                    if (exportDir.VirtualAddress != 0)
                    {
                        ParseExports(data, exportDir.VirtualAddress, sectionCount, sectionHeaderOffset, peInfo);
                    }
                }

                // Import directory (index 1)
                if (count > IMAGE_DIRECTORY_ENTRY_IMPORT)
                {
                    IMAGE_DATA_DIRECTORY importDir = BytesToStruct<IMAGE_DATA_DIRECTORY>(data, offset + (IMAGE_DIRECTORY_ENTRY_IMPORT * directorySize));
                    if (importDir.VirtualAddress != 0)
                    {
                        ParseImports(data, importDir.VirtualAddress, sectionCount, sectionHeaderOffset, peInfo);
                    }
                }

                // Security directory (index 4) - Authenticode signature
                if (count > IMAGE_DIRECTORY_ENTRY_SECURITY)
                {
                    IMAGE_DATA_DIRECTORY securityDir = BytesToStruct<IMAGE_DATA_DIRECTORY>(data, offset + (IMAGE_DIRECTORY_ENTRY_SECURITY * directorySize));
                    if (securityDir.VirtualAddress != 0)
                    {
                        peInfo.HasAuthenticode = true;
                    }
                }

                // TLS directory (index 9)
                if (count > IMAGE_DIRECTORY_ENTRY_TLS)
                {
                    IMAGE_DATA_DIRECTORY tlsDir = BytesToStruct<IMAGE_DATA_DIRECTORY>(data, offset + (IMAGE_DIRECTORY_ENTRY_TLS * directorySize));
                    if (tlsDir.VirtualAddress != 0)
                    {
                        ParseTLS(data, tlsDir.VirtualAddress, sectionCount, sectionHeaderOffset, peInfo);
                    }
                }

                // Bound Import directory (index 11)
                if (count > IMAGE_DIRECTORY_ENTRY_BOUND_IMPORT)
                {
                    IMAGE_DATA_DIRECTORY boundImportDir = BytesToStruct<IMAGE_DATA_DIRECTORY>(data, offset + (IMAGE_DIRECTORY_ENTRY_BOUND_IMPORT * directorySize));
                    if (boundImportDir.VirtualAddress != 0)
                    {
                        peInfo.HasBoundImports = true;
                        ParseBoundImports(data, boundImportDir.VirtualAddress, peInfo);
                    }
                }

                // Delay Load Import directory (index 13)
                if (count > IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT)
                {
                    IMAGE_DATA_DIRECTORY delayImportDir = BytesToStruct<IMAGE_DATA_DIRECTORY>(data, offset + (IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT * directorySize));
                    if (delayImportDir.VirtualAddress != 0)
                    {
                        peInfo.HasDelayLoadImports = true;
                        ParseDelayImports(data, delayImportDir.VirtualAddress, sectionCount, sectionHeaderOffset, peInfo);
                    }
                }
            }
            catch { }
        }

        private static void ParseImports(byte[] data, uint importRVA, int sectionCount, int sectionHeaderOffset, PEInfo peInfo)
        {
            try
            {
                int importOffset = RVAToFileOffset(data, importRVA, sectionCount, sectionHeaderOffset);
                if (importOffset == -1) return;

                int descriptorSize = Marshal.SizeOf<IMAGE_IMPORT_DESCRIPTOR>();
                int currentOffset = importOffset;

                while (currentOffset + descriptorSize < data.Length)
                {
                    IMAGE_IMPORT_DESCRIPTOR descriptor = BytesToStruct<IMAGE_IMPORT_DESCRIPTOR>(data, currentOffset);

                    if (descriptor.Name == 0) break;

                    int nameOffset = RVAToFileOffset(data, descriptor.Name, sectionCount, sectionHeaderOffset);
                    if (nameOffset == -1)
                    {
                        currentOffset += descriptorSize;
                        continue;
                    }

                    string dllName = ReadNullTerminatedString(data, nameOffset);
                    var importedDLL = new ImportedDLL
                    {
                        DLLName = dllName,
                        Category = CategorizeDLL(dllName)
                    };

                    // Parse imported functions
                    uint thunkRVA = descriptor.OriginalFirstThunk != 0 ? descriptor.OriginalFirstThunk : descriptor.FirstThunk;
                    int thunkOffset = RVAToFileOffset(data, thunkRVA, sectionCount, sectionHeaderOffset);

                    if (thunkOffset != -1)
                    {
                        ParseThunks(data, thunkOffset, peInfo.Is64Bit, sectionCount, sectionHeaderOffset, importedDLL);
                    }

                    peInfo.Imports.Add(importedDLL);
                    currentOffset += descriptorSize;
                }
            }
            catch { }
        }

        private static void ParseThunks(byte[] data, int thunkOffset, bool is64Bit, int sectionCount, int sectionHeaderOffset, ImportedDLL importedDLL)
        {
            try
            {
                int ptrSize = is64Bit ? 8 : 4;
                int currentOffset = thunkOffset;
                int maxFunctions = 5000; // Limit to prevent infinite loops
                int count = 0;

                while (currentOffset + ptrSize < data.Length && count < maxFunctions)
                {
                    ulong thunkValue = is64Bit ? BitConverter.ToUInt64(data, currentOffset) : BitConverter.ToUInt32(data, currentOffset);

                    if (thunkValue == 0) break;

                    // Check if import by ordinal
                    bool isOrdinal = is64Bit ? ((thunkValue & 0x8000000000000000) != 0) : ((thunkValue & 0x80000000) != 0);

                    if (isOrdinal)
                    {
                        ushort ordinal = (ushort)(thunkValue & 0xFFFF);
                        importedDLL.Functions.Add($"Ordinal_{ordinal}");

                        // Add detailed function info
                        importedDLL.DetailedFunctions.Add(new ImportedFunction
                        {
                            Name = $"Ordinal_{ordinal}",
                            Ordinal = ordinal,
                            IsOrdinalOnly = true,
                            Hint = 0
                        });
                    }
                    else
                    {
                        uint nameRVA = (uint)(thunkValue & (is64Bit ? 0x7FFFFFFFFFFFFFFFUL : 0x7FFFFFFFUL));
                        int nameOffset = RVAToFileOffset(data, nameRVA, sectionCount, sectionHeaderOffset);

                        if (nameOffset != -1 && nameOffset + 2 < data.Length)
                        {
                            ushort hint = BitConverter.ToUInt16(data, nameOffset);
                            string funcName = ReadNullTerminatedString(data, nameOffset + 2);

                            if (!string.IsNullOrEmpty(funcName))
                            {
                                importedDLL.Functions.Add(funcName);

                                // Add detailed function info
                                importedDLL.DetailedFunctions.Add(new ImportedFunction
                                {
                                    Name = funcName,
                                    Ordinal = 0,
                                    IsOrdinalOnly = false,
                                    Hint = hint,
                                    BehaviorCategory = CategorizeAPIBehavior(funcName),
                                    MITRETechnique = MapToMITRE(funcName)
                                });
                            }
                        }
                    }

                    currentOffset += ptrSize;
                    count++;
                }
            }
            catch { }
        }

        private static void ParseExports(byte[] data, uint exportRVA, int sectionCount, int sectionHeaderOffset, PEInfo peInfo)
        {
            try
            {
                int exportOffset = RVAToFileOffset(data, exportRVA, sectionCount, sectionHeaderOffset);
                if (exportOffset == -1) return;

                IMAGE_EXPORT_DIRECTORY exportDir = BytesToStruct<IMAGE_EXPORT_DIRECTORY>(data, exportOffset);

                int functionsOffset = RVAToFileOffset(data, exportDir.AddressOfFunctions, sectionCount, sectionHeaderOffset);
                int namesOffset = RVAToFileOffset(data, exportDir.AddressOfNames, sectionCount, sectionHeaderOffset);
                int ordinalsOffset = RVAToFileOffset(data, exportDir.AddressOfNameOrdinals, sectionCount, sectionHeaderOffset);

                if (functionsOffset == -1 || namesOffset == -1 || ordinalsOffset == -1) return;

                for (int i = 0; i < exportDir.NumberOfNames && i < 10000; i++)
                {
                    try
                    {
                        uint nameRVA = BitConverter.ToUInt32(data, namesOffset + (i * 4));
                        int nameOffset = RVAToFileOffset(data, nameRVA, sectionCount, sectionHeaderOffset);

                        if (nameOffset == -1) continue;

                        string funcName = ReadNullTerminatedString(data, nameOffset);
                        ushort ordinal = BitConverter.ToUInt16(data, ordinalsOffset + (i * 2));
                        uint functionRVA = BitConverter.ToUInt32(data, functionsOffset + (ordinal * 4));

                        peInfo.Exports.Add(new ExportedFunction
                        {
                            Name = funcName,
                            Ordinal = (uint)(exportDir.Base + ordinal),
                            RVA = functionRVA
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void ParseSections(byte[] data, int sectionHeaderOffset, int sectionCount, PEInfo peInfo)
        {
            try
            {
                int sectionSize = Marshal.SizeOf<IMAGE_SECTION_HEADER>();

                for (int i = 0; i < sectionCount; i++)
                {
                    IMAGE_SECTION_HEADER section = BytesToStruct<IMAGE_SECTION_HEADER>(data, sectionHeaderOffset + (i * sectionSize));

                    string sectionName = Encoding.ASCII.GetString(section.Name).TrimEnd('\0');

                    var sectionInfo = new SectionInfo
                    {
                        Name = sectionName,
                        VirtualAddress = section.VirtualAddress,
                        VirtualSize = section.VirtualSize,
                        RawSize = section.SizeOfRawData,
                        Characteristics = section.Characteristics,
                        Flags = GetSectionFlags(section.Characteristics)
                    };

                    // Calculate entropy
                    if (section.PointerToRawData > 0 && section.SizeOfRawData > 0 &&
                        section.PointerToRawData + section.SizeOfRawData <= data.Length)
                    {
                        byte[] sectionData = new byte[section.SizeOfRawData];
                        Array.Copy(data, section.PointerToRawData, sectionData, 0, section.SizeOfRawData);
                        sectionInfo.Entropy = CalculateEntropy(sectionData);
                    }

                    peInfo.Sections.Add(sectionInfo);
                }
            }
            catch { }
        }

        private static double CalculateEntropy(byte[] data)
        {
            if (data.Length == 0) return 0;

            var frequency = new int[256];
            foreach (byte b in data)
            {
                frequency[b]++;
            }

            double entropy = 0;
            foreach (int count in frequency)
            {
                if (count == 0) continue;
                double probability = (double)count / data.Length;
                entropy -= probability * Math.Log(probability, 2);
            }

            return entropy;
        }

        private static void DetectPackers(byte[] data, PEInfo peInfo)
        {
            var detectedPackers = new HashSet<string>();

            // Section-based detection
            foreach (var section in peInfo.Sections)
            {
                string name = section.Name.ToUpper();

                // UPX - Multiple signatures
                if (name.StartsWith("UPX") || name == "UPX0" || name == "UPX1" || name == "UPX2")
                    detectedPackers.Add("UPX");

                // Themida/WinLicense - Commercial protector
                if (name.Contains(".THEMIDA") || name.Contains(".WINLICE") || name.Contains("THEMIDA") || name.Contains(".OREANS"))
                    detectedPackers.Add("Themida/WinLicense");

                // VMProtect - Virtualization-based protector
                if (name.Contains(".VMPROT") || name.Contains(".VMP") || name == ".VMP0" || name == ".VMP1")
                    detectedPackers.Add("VMProtect");

                // Enigma Protector
                if (name.Contains(".ENIGMA") || name == ".ENIGMA1" || name == ".ENIGMA2")
                    detectedPackers.Add("Enigma Protector");

                // ASProtect/ASPack
                if (name.Contains(".ASPACK") || name.Contains(".ASP") || name.StartsWith("ASP"))
                    detectedPackers.Add("ASProtect/ASPack");

                // PECompact
                if (name.Contains("PECOMPACT") || name.Contains("PEC2"))
                    detectedPackers.Add("PECompact");

                // Obsidium
                if (name.Contains(".OBSIDIUM") || name.Contains("OBSIDIUM"))
                    detectedPackers.Add("Obsidium");

                // Code Virtualizer
                if (name.Contains(".CODEVIRTUALIZER") || name.Contains("CV"))
                    detectedPackers.Add("Code Virtualizer");

                // Safengine
                if (name.Contains(".SAFENGINE") || name.Contains("SAFENG"))
                    detectedPackers.Add("Safengine Shielden");

                // Armadillo
                if (name.Contains(".ARM") || name == "ARM!")
                    detectedPackers.Add("Armadillo");

                // FSG
                if (name == "FSG!" || name.Contains("FSG"))
                    detectedPackers.Add("FSG");

                // Petite
                if (name.Contains(".PETITE") || name == "Petite")
                    detectedPackers.Add("Petite");

                // NSPack
                if (name.Contains(".NSPAC") || name.Contains("NSPACK"))
                    detectedPackers.Add("NSPack");

                // MEW
                if (name == "MEW" || name.Contains("MEW"))
                    detectedPackers.Add("MEW");

                // High entropy detection - Indicates encryption/compression
                if (section.Entropy > 7.2)
                {
                    if (name == ".TEXT" || name == ".CODE")
                        detectedPackers.Add("High Entropy .text (Packed/Encrypted)");
                    else if (section.Entropy > 7.8)
                        detectedPackers.Add($"Very High Entropy in {section.Name} (Encrypted)");
                }
            }

            // Import-based detection
            foreach (var import in peInfo.Imports)
            {
                string dll = import.DLLName.ToLower();

                // Themida
                if (dll.Contains("themida") || dll.Contains("winlicense"))
                    detectedPackers.Add("Themida/WinLicense");

                // VMProtect
                if (dll.Contains("vmprotect"))
                    detectedPackers.Add("VMProtect");
            }

            // Entry point detection - Unusual entry points may indicate packing
            if (peInfo.EntryPoint < 0x1000 || peInfo.EntryPoint > 0x100000)
            {
                detectedPackers.Add("Unusual Entry Point (Possible packer)");
            }

            // Section count heuristic
            if (peInfo.Sections.Count <= 2)
            {
                detectedPackers.Add("Few Sections (Possible packer)");
            }

            // Check for raw size vs virtual size mismatch (common in packed files)
            foreach (var section in peInfo.Sections)
            {
                if (section.VirtualSize > 0 && section.RawSize > 0)
                {
                    double ratio = (double)section.VirtualSize / section.RawSize;
                    if (ratio > 2.0) // Virtual size much larger than raw
                    {
                        detectedPackers.Add($"Size Mismatch in {section.Name} (Compressed section)");
                        break;
                    }
                }
            }

            peInfo.DetectedPackers = detectedPackers.ToList();
        }

        private static string CategorizeDLL(string dllName)
        {
            string dll = dllName.ToLower();

            if (dll.Contains("kernel") || dll.Contains("ntdll") || dll.Contains("hal"))
                return "Kernel/System";
            if (dll.Contains("user32") || dll.Contains("gdi32") || dll.Contains("comctl"))
                return "UI/Graphics";
            if (dll.Contains("ws2") || dll.Contains("wininet") || dll.Contains("winhttp") || dll.Contains("mswsock"))
                return "Network";
            if (dll.Contains("d3d") || dll.Contains("opengl") || dll.Contains("vulkan") || dll.Contains("dxgi"))
                return "Graphics API";
            if (dll.Contains("xinput") || dll.Contains("dinput"))
                return "Input";
            if (dll.Contains("dsound") || dll.Contains("winmm") || dll.Contains("xaudio"))
                return "Audio";
            if (dll.Contains("crypt") || dll.Contains("bcrypt"))
                return "Cryptography";
            if (dll.Contains("advapi") || dll.Contains("secur32"))
                return "Security";
            if (dll.Contains("shell32") || dll.Contains("shlwapi"))
                return "Shell";
            if (dll.Contains("msvc") || dll.Contains("ucrt"))
                return "C Runtime";

            return "Other";
        }

        private static string GetSectionFlags(uint characteristics)
        {
            var flags = new List<string>();

            if ((characteristics & 0x20000000) != 0) flags.Add("Executable");
            if ((characteristics & 0x40000000) != 0) flags.Add("Readable");
            if ((characteristics & 0x80000000) != 0) flags.Add("Writable");
            if ((characteristics & 0x00000020) != 0) flags.Add("Code");
            if ((characteristics & 0x00000040) != 0) flags.Add("InitializedData");
            if ((characteristics & 0x00000080) != 0) flags.Add("UninitializedData");

            return string.Join(", ", flags);
        }

        private static string GetSubsystemName(ushort subsystem)
        {
            return subsystem switch
            {
                1 => "Native",
                2 => "Windows GUI",
                3 => "Windows CUI (Console)",
                5 => "OS/2 CUI",
                7 => "POSIX CUI",
                9 => "Windows CE GUI",
                10 => "EFI Application",
                11 => "EFI Boot Service Driver",
                12 => "EFI Runtime Driver",
                13 => "EFI ROM",
                14 => "XBOX",
                16 => "Windows Boot Application",
                _ => $"Unknown ({subsystem})"
            };
        }

        private static int RVAToFileOffset(byte[] data, uint rva, int sectionCount, int sectionHeaderOffset)
        {
            try
            {
                int sectionSize = Marshal.SizeOf<IMAGE_SECTION_HEADER>();

                for (int i = 0; i < sectionCount; i++)
                {
                    IMAGE_SECTION_HEADER section = BytesToStruct<IMAGE_SECTION_HEADER>(data, sectionHeaderOffset + (i * sectionSize));

                    if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.VirtualSize)
                    {
                        return (int)(rva - section.VirtualAddress + section.PointerToRawData);
                    }
                }

                return -1;
            }
            catch
            {
                return -1;
            }
        }

        private static string ReadNullTerminatedString(byte[] data, int offset)
        {
            try
            {
                var sb = new StringBuilder();
                int maxLength = 256;

                for (int i = 0; i < maxLength && offset + i < data.Length; i++)
                {
                    byte b = data[offset + i];
                    if (b == 0) break;
                    sb.Append((char)b);
                }

                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Parse TLS (Thread Local Storage) callbacks - Often used for anti-debugging
        /// </summary>
        private static void ParseTLS(byte[] data, uint tlsRVA, int sectionCount, int sectionHeaderOffset, PEInfo peInfo)
        {
            try
            {
                int tlsOffset = RVAToFileOffset(data, tlsRVA, sectionCount, sectionHeaderOffset);
                if (tlsOffset == -1) return;

                if (peInfo.Is64Bit)
                {
                    IMAGE_TLS_DIRECTORY64 tlsDir = BytesToStruct<IMAGE_TLS_DIRECTORY64>(data, tlsOffset);
                    if (tlsDir.AddressOfCallBacks != 0)
                    {
                        peInfo.HasTLSCallbacks = true;
                        // Note: Reading callback addresses would require mapping virtual addresses
                        // which is complex without loading the PE. Just flag presence for now.
                    }
                }
                else
                {
                    IMAGE_TLS_DIRECTORY32 tlsDir = BytesToStruct<IMAGE_TLS_DIRECTORY32>(data, tlsOffset);
                    if (tlsDir.AddressOfCallBacks != 0)
                    {
                        peInfo.HasTLSCallbacks = true;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Detect overlay data (extra data appended after PE sections)
        /// </summary>
        private static void DetectOverlay(byte[] data, PEInfo peInfo)
        {
            try
            {
                if (peInfo.Sections.Count == 0) return;

                // Find the last section's end
                uint lastSectionEnd = 0;
                foreach (var section in peInfo.Sections)
                {
                    uint sectionEnd = section.RawSize + section.VirtualAddress;
                    if (sectionEnd > lastSectionEnd)
                        lastSectionEnd = sectionEnd;
                }

                // Check if there's data after the last section
                if (data.Length > lastSectionEnd)
                {
                    peInfo.HasOverlay = true;
                    peInfo.OverlaySize = (int)(data.Length - lastSectionEnd);
                }
            }
            catch { }
        }

        /// <summary>
        /// Calculate Import Hash (ImpHash) for malware identification
        /// </summary>
        private static void CalculateImpHash(PEInfo peInfo)
        {
            try
            {
                if (peInfo.Imports.Count == 0)
                {
                    peInfo.ImpHash = "N/A";
                    return;
                }

                // Build normalized import string
                var sb = new StringBuilder();
                foreach (var dll in peInfo.Imports.OrderBy(d => d.DLLName.ToLower()))
                {
                    string dllName = dll.DLLName.ToLower().Replace(".dll", "");
                    foreach (var func in dll.Functions.OrderBy(f => f.ToLower()))
                    {
                        if (!string.IsNullOrEmpty(func))
                        {
                            sb.Append($"{dllName}.{func.ToLower()},");
                        }
                    }
                }

                // Calculate MD5 hash of the normalized string
                if (sb.Length > 0)
                {
                    using (var md5 = System.Security.Cryptography.MD5.Create())
                    {
                        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                        peInfo.ImpHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                    }
                }
                else
                {
                    peInfo.ImpHash = "N/A";
                }
            }
            catch
            {
                peInfo.ImpHash = "Error";
            }
        }

        /// <summary>
        /// Detect suspicious indicators that may suggest malware
        /// </summary>
        private static void DetectSuspiciousIndicators(PEInfo peInfo)
        {
            var indicators = new List<string>();

            // Check for TLS callbacks (common anti-debugging)
            if (peInfo.HasTLSCallbacks)
            {
                indicators.Add("TLS Callbacks present (Anti-debugging technique)");
            }

            // Check for suspicious imports
            var suspiciousAPIs = new Dictionary<string, string>
            {
                ["VirtualAllocEx"] = "Process injection",
                ["WriteProcessMemory"] = "Process injection",
                ["CreateRemoteThread"] = "Process injection",
                ["NtWriteVirtualMemory"] = "Process injection",
                ["QueueUserAPC"] = "APC injection",
                ["SetWindowsHookEx"] = "Hooking",
                ["GetAsyncKeyState"] = "Keylogging",
                ["GetForegroundWindow"] = "Keylogging",
                ["CreateToolhelp32Snapshot"] = "Process enumeration",
                ["IsDebuggerPresent"] = "Anti-debugging",
                ["CheckRemoteDebuggerPresent"] = "Anti-debugging",
                ["NtQueryInformationProcess"] = "Anti-debugging",
                ["OutputDebugString"] = "Anti-debugging",
                ["GetTickCount"] = "Anti-debugging (timing)",
                ["QueryPerformanceCounter"] = "Anti-debugging (timing)",
                ["CryptAcquireContext"] = "Cryptography (ransomware?)",
                ["InternetOpen"] = "Network communication",
                ["URLDownloadToFile"] = "File downloading",
                ["WinExec"] = "Command execution",
                ["ShellExecute"] = "Command execution",
                ["RegSetValueEx"] = "Registry modification",
                ["RegCreateKeyEx"] = "Registry modification"
            };

            foreach (var import in peInfo.Imports)
            {
                foreach (var func in import.Functions)
                {
                    if (suspiciousAPIs.ContainsKey(func))
                    {
                        indicators.Add($"{func} - {suspiciousAPIs[func]}");
                    }
                }
            }

            // Check for no imports (packed or shellcode)
            if (peInfo.Imports.Count == 0)
            {
                indicators.Add("No imports (Packed or shellcode)");
            }

            // Check for writable and executable sections
            foreach (var section in peInfo.Sections)
            {
                if (section.Flags.Contains("Writable") && section.Flags.Contains("Executable"))
                {
                    indicators.Add($"Writable+Executable section: {section.Name} (Code injection target)");
                }
            }

            // Check for unusual section names
            var normalSections = new[] { ".text", ".data", ".rdata", ".bss", ".rsrc", ".reloc", ".idata", ".edata", ".pdata", ".tls" };
            foreach (var section in peInfo.Sections)
            {
                string name = section.Name.ToLower().TrimEnd('\0');
                if (!normalSections.Contains(name) && !string.IsNullOrWhiteSpace(name))
                {
                    // Check if it's a known packer section
                    if (!name.Contains("upx") && !name.Contains("themida") && !name.Contains("vmp"))
                    {
                        indicators.Add($"Unusual section name: {section.Name}");
                    }
                }
            }

            // Check for missing ASLR/DEP
            if (!peInfo.HasASLR)
            {
                indicators.Add("No ASLR (Old or deliberately weakened binary)");
            }
            if (!peInfo.HasDEP)
            {
                indicators.Add("No DEP (Old or deliberately weakened binary)");
            }

            // Check compilation time (very old or future dates)
            if (peInfo.CompilationTime.Year < 2000)
            {
                indicators.Add($"Suspicious compilation time: {peInfo.CompilationTime} (Too old)");
            }
            else if (peInfo.CompilationTime > DateTime.Now.AddDays(1))
            {
                indicators.Add($"Suspicious compilation time: {peInfo.CompilationTime} (Future date)");
            }

            peInfo.SuspiciousIndicators = indicators;
        }

        /// <summary>
        /// Check for Authenticode signature presence and extract info
        /// </summary>
        private static void CheckAuthenticode(byte[] data, PEInfo peInfo)
        {
            try
            {
                if (peInfo.HasAuthenticode)
                {
                    peInfo.SignatureInfo = "Present (Details require WinVerifyTrust API)";
                }
                else
                {
                    peInfo.SignatureInfo = "Not signed";
                }
            }
            catch
            {
                peInfo.SignatureInfo = "Error checking signature";
            }
        }

        /// <summary>
        /// Parse Rich Header for compiler information
        /// </summary>
        private static void ParseRichHeader(byte[] data, int ntHeaderOffset, PEInfo peInfo)
        {
            try
            {
                // Rich header is between DOS header and NT header
                // Search backwards from NT header for "Rich" signature
                for (int i = ntHeaderOffset - 4; i >= 0x80; i -= 4)
                {
                    uint signature = BitConverter.ToUInt32(data, i);
                    if (signature == RICH_SIGNATURE)
                    {
                        uint xorKey = BitConverter.ToUInt32(data, i + 4);

                        var richInfo = new RichHeaderInfo { IsValid = true };

                        // Find DanS signature (start of Rich data)
                        int dansPos = -1;
                        for (int j = 0x80; j < i; j += 4)
                        {
                            uint dansTest = BitConverter.ToUInt32(data, j) ^ xorKey;
                            if (dansTest == DANS_SIGNATURE)
                            {
                                dansPos = j;
                                break;
                            }
                        }

                        if (dansPos != -1)
                        {
                            // Parse compiler entries
                            for (int j = dansPos + 16; j < i; j += 8)
                            {
                                uint entry1 = BitConverter.ToUInt32(data, j) ^ xorKey;
                                uint entry2 = BitConverter.ToUInt32(data, j + 4) ^ xorKey;

                                if (entry1 == 0 && entry2 == 0) break;

                                ushort prodId = (ushort)(entry1 >> 16);
                                ushort buildNum = (ushort)(entry1 & 0xFFFF);
                                int useCount = (int)entry2;

                                richInfo.Compilers.Add(new CompilerEntry
                                {
                                    ProductId = prodId,
                                    BuildNumber = buildNum,
                                    UseCount = useCount,
                                    ProductName = GetProductName(prodId)
                                });
                            }

                            richInfo.Checksum = xorKey.ToString("X8");
                            peInfo.RichHeader = richInfo;

                            // Build compiler info string
                            if (richInfo.Compilers.Count > 0)
                            {
                                var primary = richInfo.Compilers.OrderByDescending(c => c.UseCount).First();
                                peInfo.CompilerInfo = $"{primary.ProductName} (Build {primary.BuildNumber})";
                            }
                        }
                        break;
                    }
                }
            }
            catch { }
        }

        private static string GetProductName(ushort prodId)
        {
            // Known MSVC product IDs
            return prodId switch
            {
                0x0001 => "Import0",
                0x0002 => "Linker510",
                0x0004 => "Cvtomf510",
                0x0005 => "Linker600",
                0x0006 => "Cvtomf600",
                0x0007 => "Cvtres500",
                0x0009 => "Utc11_Basic",
                0x000a => "Utc11_C",
                0x000b => "Utc12_Basic",
                0x000c => "Utc12_C",
                0x000d => "Utc12_CPP",
                0x000e => "AliasObj60",
                0x0010 => "VisualBasic60",
                0x0013 => "Masm613",
                0x0014 => "Masm710",
                0x0015 => "Linker511",
                0x0017 => "Resource",
                0x0019 => "AliasObj70",
                0x001c => "Linker620",
                0x001d => "Cvtomf620",
                0x001e => "Export_Client",
                0x005a => "Masm614",
                0x005b => "Masm615",
                0x005d => "Linker622",
                0x005e => "Linker700",
                0x005f => "Export700",
                0x0069 => "Masm800",
                0x006d => "Linker900",
                0x0083 => "Masm1000",
                0x0091 => "Linker1000",
                0x0094 => "Utc18_C",
                0x0095 => "Utc18_CPP",
                0x009a => "Utc19_C",
                0x009b => "Utc19_CPP",
                0x00aa => "Utc1900_C",
                0x00ab => "Utc1900_CPP",
                0x00c1 => "Linker1100",
                0x00c6 => "Utc1910_C",
                0x00c7 => "Utc1910_CPP",
                _ => $"Unknown_{prodId:X4}"
            };
        }

        /// <summary>
        /// Parse delay-load imports
        /// </summary>
        private static void ParseDelayImports(byte[] data, uint delayImportRVA, int sectionCount, int sectionHeaderOffset, PEInfo peInfo)
        {
            try
            {
                int delayOffset = RVAToFileOffset(data, delayImportRVA, sectionCount, sectionHeaderOffset);
                if (delayOffset == -1) return;

                int descriptorSize = Marshal.SizeOf<IMAGE_DELAYLOAD_DESCRIPTOR>();
                int currentOffset = delayOffset;

                while (currentOffset + descriptorSize < data.Length)
                {
                    IMAGE_DELAYLOAD_DESCRIPTOR descriptor = BytesToStruct<IMAGE_DELAYLOAD_DESCRIPTOR>(data, currentOffset);

                    if (descriptor.DllNameRVA == 0) break;

                    int nameOffset = RVAToFileOffset(data, descriptor.DllNameRVA, sectionCount, sectionHeaderOffset);
                    if (nameOffset == -1)
                    {
                        currentOffset += descriptorSize;
                        continue;
                    }

                    string dllName = ReadNullTerminatedString(data, nameOffset);
                    var delayDLL = new DelayLoadDLL
                    {
                        DLLName = dllName,
                        Reason = "Loaded on first use (optimization or anti-detection)"
                    };

                    // Parse function names from INT
                    if (descriptor.ImportNameTableRVA != 0)
                    {
                        int intOffset = RVAToFileOffset(data, descriptor.ImportNameTableRVA, sectionCount, sectionHeaderOffset);
                        if (intOffset != -1)
                        {
                            // Parse similar to normal imports
                            int ptrSize = 4; // Delay imports typically use 32-bit
                            int thunkOffset = intOffset;
                            int count = 0;
                            while (thunkOffset + ptrSize < data.Length && count < 1000)
                            {
                                uint thunkValue = BitConverter.ToUInt32(data, thunkOffset);
                                if (thunkValue == 0) break;

                                if ((thunkValue & 0x80000000) == 0)
                                {
                                    int funcNameOffset = RVAToFileOffset(data, thunkValue + 2, sectionCount, sectionHeaderOffset);
                                    if (funcNameOffset != -1 && funcNameOffset < data.Length)
                                    {
                                        string funcName = ReadNullTerminatedString(data, funcNameOffset);
                                        if (!string.IsNullOrEmpty(funcName))
                                        {
                                            delayDLL.Functions.Add(funcName);
                                        }
                                    }
                                }

                                thunkOffset += ptrSize;
                                count++;
                            }
                        }
                    }

                    peInfo.DelayImports.Add(delayDLL);
                    currentOffset += descriptorSize;
                }
            }
            catch { }
        }

        /// <summary>
        /// Parse bound imports
        /// </summary>
        private static void ParseBoundImports(byte[] data, uint boundImportOffset, PEInfo peInfo)
        {
            try
            {
                int descriptorSize = Marshal.SizeOf<IMAGE_BOUND_IMPORT_DESCRIPTOR>();
                int currentOffset = (int)boundImportOffset;

                while (currentOffset + descriptorSize < data.Length)
                {
                    IMAGE_BOUND_IMPORT_DESCRIPTOR descriptor = BytesToStruct<IMAGE_BOUND_IMPORT_DESCRIPTOR>(data, currentOffset);

                    if (descriptor.TimeDateStamp == 0 && descriptor.OffsetModuleName == 0) break;

                    // Find matching import and mark as bound
                    if (descriptor.OffsetModuleName > 0)
                    {
                        string dllName = ReadNullTerminatedString(data, (int)boundImportOffset + descriptor.OffsetModuleName);

                        foreach (var import in peInfo.Imports)
                        {
                            if (import.DLLName.Equals(dllName, StringComparison.OrdinalIgnoreCase))
                            {
                                import.IsBound = true;
                                import.BoundTimestamp = descriptor.TimeDateStamp;
                                break;
                            }
                        }
                    }

                    currentOffset += descriptorSize;
                }
            }
            catch { }
        }

        /// <summary>
        /// Categorize API by behavior (File, Process, Network, etc.)
        /// </summary>
        private static string CategorizeAPIBehavior(string apiName)
        {
            string lower = apiName.ToLower();

            if (lower.Contains("file") || lower.Contains("read") || lower.Contains("write") ||
                lower.Contains("open") || lower.Contains("close") || lower.Contains("create"))
                return "File Operations";

            if (lower.Contains("process") || lower.Contains("thread") || lower.Contains("inject") ||
                lower.Contains("allocex") || lower.Contains("writeprocessmemory") || lower.Contains("remotethread"))
                return "Process/Thread";

            if (lower.Contains("socket") || lower.Contains("internet") || lower.Contains("http") ||
                lower.Contains("url") || lower.Contains("connect") || lower.Contains("send") || lower.Contains("recv"))
                return "Network";

            if (lower.Contains("reg") && (lower.Contains("key") || lower.Contains("value")))
                return "Registry";

            if (lower.Contains("crypt") || lower.Contains("hash") || lower.Contains("encrypt") || lower.Contains("decrypt"))
                return "Cryptography";

            if (lower.Contains("debug") || lower.Contains("breakpoint") || lower.Contains("context"))
                return "Debugging";

            if (lower.Contains("memory") || lower.Contains("virtual") || lower.Contains("heap"))
                return "Memory";

            if (lower.Contains("service") || lower.Contains("driver"))
                return "System Service";

            return "Other";
        }

        /// <summary>
        /// Map API to MITRE ATT&CK technique
        /// </summary>
        private static string MapToMITRE(string apiName)
        {
            string lower = apiName.ToLower();

            if (lower.Contains("writeprocessmemory") || lower.Contains("createremotethread") ||
                lower.Contains("queueuserapc") || lower.Contains("ntwritevirtualmemory"))
                return "T1055 - Process Injection";

            if (lower.Contains("isdebugger") || lower.Contains("checkremotedebugger") ||
                lower.Contains("ntqueryinformation") || lower.Contains("outputdebugstring"))
                return "T1622 - Debugger Evasion";

            if (lower.Contains("regsetvalue") || lower.Contains("regcreatekey"))
                return "T1112 - Modify Registry";

            if (lower.Contains("createservice") || lower.Contains("startservice"))
                return "T1543 - Create/Modify Service";

            if (lower.Contains("internetopen") || lower.Contains("urldownload") ||
                lower.Contains("httpopen") || lower.Contains("winhttp"))
                return "T1105 - Ingress Tool Transfer";

            if (lower.Contains("getasynckeystate") || lower.Contains("getforegroundwindow"))
                return "T1056 - Input Capture";

            if (lower.Contains("cryptacquirecontext") || lower.Contains("cryptencrypt"))
                return "T1486 - Data Encrypted for Impact";

            if (lower.Contains("toolhelp32") || lower.Contains("enumprocesses"))
                return "T1057 - Process Discovery";

            if (lower.Contains("setwindowshook"))
                return "T1179 - Hooking";

            if (lower.Contains("shellexecute") || lower.Contains("winexec") || lower.Contains("createprocess"))
                return "T1106 - Native API Execution";

            return "";
        }

        /// <summary>
        /// Analyze import patterns and categorize APIs
        /// </summary>
        private static void AnalyzeImportPatterns(PEInfo peInfo)
        {
            try
            {
                var categories = new Dictionary<string, int>();
                var mitrePatterns = new HashSet<string>();
                int ordinalCount = 0;

                foreach (var dll in peInfo.Imports)
                {
                    foreach (var func in dll.DetailedFunctions)
                    {
                        // Count ordinal imports
                        if (func.IsOrdinalOnly)
                            ordinalCount++;

                        // Categorize
                        string category = func.BehaviorCategory;
                        if (!string.IsNullOrEmpty(category))
                        {
                            if (!categories.ContainsKey(category))
                                categories[category] = 0;
                            categories[category]++;
                        }

                        // Track MITRE techniques
                        if (!string.IsNullOrEmpty(func.MITRETechnique))
                        {
                            mitrePatterns.Add(func.MITRETechnique);
                        }
                    }
                }

                peInfo.OrdinalOnlyImports = ordinalCount;
                peInfo.APICategories = categories;
                peInfo.MITREPatterns = mitrePatterns.ToList();
            }
            catch { }
        }

        /// <summary>
        /// Analyze export forwarding chains
        /// </summary>
        private static void AnalyzeExportForwarding(byte[] data, int sectionCount, int sectionHeaderOffset, PEInfo peInfo)
        {
            try
            {
                if (peInfo.Exports.Count == 0) return;

                // Check if any exports are forwarded
                foreach (var export in peInfo.Exports)
                {
                    // Check if RVA points to export section (forwarded export)
                    int exportOffset = RVAToFileOffset(data, export.RVA, sectionCount, sectionHeaderOffset);
                    if (exportOffset != -1 && exportOffset < data.Length)
                    {
                        // Read potential forwarder string
                        string potential = ReadNullTerminatedString(data, exportOffset);
                        if (!string.IsNullOrEmpty(potential) && potential.Contains("."))
                        {
                            // This looks like a forwarder (e.g., "NTDLL.RtlAllocateHeap")
                            export.IsForwarded = true;
                            export.ForwarderChain = potential;
                            peInfo.HasForwardedExports = true;
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Build dependency tree from imports
        /// </summary>
        private static void BuildDependencyTree(PEInfo peInfo)
        {
            try
            {
                var deps = new DependencyAnalysis
                {
                    TotalDependencies = peInfo.Imports.Count
                };

                // Build simple dependency tree (DLL -> count of functions)
                foreach (var import in peInfo.Imports)
                {
                    deps.DependencyTree[import.DLLName] = import.Functions.Count;
                }

                // Check for common missing dependencies (simplified)
                var commonDlls = new[] { "kernel32.dll", "user32.dll", "ntdll.dll" };
                foreach (var dll in commonDlls)
                {
                    if (!peInfo.Imports.Any(i => i.DLLName.Equals(dll, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Not importing common DLL might indicate static linking or packing
                        if (dll == "kernel32.dll")
                        {
                            deps.MissingDependencies.Add($"{dll} (Possibly statically linked or packed)");
                        }
                    }
                }

                // Calculate max depth (simplified - just count levels)
                deps.MaxDepth = peInfo.Imports.Count > 0 ? 2 : 0; // Simplified

                peInfo.Dependencies = deps;
            }
            catch { }
        }

        private static T BytesToStruct<T>(byte[] data, int offset = 0) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            if (offset + size > data.Length)
                throw new ArgumentException("Not enough data");

            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(data, offset, ptr, size);
                return Marshal.PtrToStructure<T>(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }
}
