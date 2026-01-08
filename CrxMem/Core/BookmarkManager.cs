using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CrxMem.Core
{
    /// <summary>
    /// Represents a bookmarked address in the disassembler
    /// </summary>
    public class Bookmark
    {
        public long Address { get; set; }
        public string Label { get; set; } = "";
        public string Comment { get; set; } = "";
        public string Instruction { get; set; } = "";
        public string Module { get; set; } = "";
        /// <summary>
        /// Original address string in module+offset format (e.g., "Gunz.exe+29DF28")
        /// Used to re-resolve the address after game restart (ASLR)
        /// </summary>
        public string OriginalAddressString { get; set; } = "";
        public DateTime Created { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Manages bookmarks with persistence to JSON file
    /// </summary>
    public static class BookmarkManager
    {
        private static List<Bookmark> _bookmarks = new();
        private static readonly string _saveFolder;
        private static readonly string _saveFile;

        public static event Action? BookmarksChanged;

        static BookmarkManager()
        {
            _saveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrxMem");
            _saveFile = Path.Combine(_saveFolder, "bookmarks.json");
            Load();
        }

        /// <summary>
        /// Add a bookmark (replaces existing if same address)
        /// </summary>
        public static void Add(Bookmark bookmark)
        {
            // Remove existing bookmark at same address
            _bookmarks.RemoveAll(b => b.Address == bookmark.Address);
            _bookmarks.Add(bookmark);
            Save();
            BookmarksChanged?.Invoke();
        }

        /// <summary>
        /// Remove a bookmark by address
        /// </summary>
        public static void Remove(long address)
        {
            int removed = _bookmarks.RemoveAll(b => b.Address == address);
            if (removed > 0)
            {
                Save();
                BookmarksChanged?.Invoke();
            }
        }

        /// <summary>
        /// Get all bookmarks
        /// </summary>
        public static IReadOnlyList<Bookmark> GetAll() => _bookmarks.AsReadOnly();

        /// <summary>
        /// Check if an address is bookmarked
        /// </summary>
        public static bool Exists(long address) => _bookmarks.Any(b => b.Address == address);

        /// <summary>
        /// Get a bookmark by address
        /// </summary>
        public static Bookmark? Get(long address) => _bookmarks.FirstOrDefault(b => b.Address == address);

        /// <summary>
        /// Update a bookmark's label
        /// </summary>
        public static void UpdateLabel(long address, string label)
        {
            var bookmark = _bookmarks.FirstOrDefault(b => b.Address == address);
            if (bookmark != null)
            {
                bookmark.Label = label;
                Save();
                BookmarksChanged?.Invoke();
            }
        }

        /// <summary>
        /// Update a bookmark's comment
        /// </summary>
        public static void UpdateComment(long address, string comment)
        {
            var bookmark = _bookmarks.FirstOrDefault(b => b.Address == address);
            if (bookmark != null)
            {
                bookmark.Comment = comment;
                Save();
                BookmarksChanged?.Invoke();
            }
        }

        /// <summary>
        /// Save bookmarks to JSON file
        /// </summary>
        public static void Save()
        {
            try
            {
                if (!Directory.Exists(_saveFolder))
                    Directory.CreateDirectory(_saveFolder);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_bookmarks, options);
                File.WriteAllText(_saveFile, json);
            }
            catch
            {
                // Ignore save errors
            }
        }

        /// <summary>
        /// Load bookmarks from JSON file
        /// </summary>
        public static void Load()
        {
            try
            {
                if (File.Exists(_saveFile))
                {
                    string json = File.ReadAllText(_saveFile);
                    var loaded = JsonSerializer.Deserialize<List<Bookmark>>(json);
                    if (loaded != null)
                    {
                        _bookmarks = loaded;
                    }
                }
            }
            catch
            {
                _bookmarks = new List<Bookmark>();
            }
        }

        /// <summary>
        /// Clear all bookmarks
        /// </summary>
        public static void Clear()
        {
            _bookmarks.Clear();
            Save();
            BookmarksChanged?.Invoke();
        }

        /// <summary>
        /// Re-resolve all bookmark addresses from their OriginalAddressString (module+offset)
        /// Call this when attaching to a process to update addresses after ASLR
        /// </summary>
        /// <param name="process">The ProcessAccess to use for module lookup</param>
        public static void ResolveAddresses(ProcessAccess process)
        {
            if (process == null) return;

            bool anyChanged = false;
            foreach (var bookmark in _bookmarks)
            {
                if (!string.IsNullOrEmpty(bookmark.OriginalAddressString) &&
                    bookmark.OriginalAddressString.Contains('+'))
                {
                    var resolved = ResolveModuleOffsetAddress(process, bookmark.OriginalAddressString);
                    if (resolved != 0 && resolved != bookmark.Address)
                    {
                        bookmark.Address = resolved;
                        anyChanged = true;
                    }
                }
            }

            if (anyChanged)
            {
                Save();
                BookmarksChanged?.Invoke();
            }
        }

        /// <summary>
        /// Resolve a module+offset address string to an absolute address
        /// </summary>
        private static long ResolveModuleOffsetAddress(ProcessAccess process, string addressString)
        {
            if (process == null || string.IsNullOrEmpty(addressString))
                return 0;

            int plusIndex = addressString.IndexOf('+');
            if (plusIndex <= 0 || plusIndex >= addressString.Length - 1)
                return 0;

            string moduleName = addressString.Substring(0, plusIndex).Trim();
            string offsetStr = addressString.Substring(plusIndex + 1).Trim();

            // Remove common prefixes from offset
            if (offsetStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                offsetStr = offsetStr.Substring(2);

            // Parse offset as hex
            if (!long.TryParse(offsetStr, System.Globalization.NumberStyles.HexNumber, null, out long offset))
                return 0;

            // Find the module
            var modules = process.GetModules();
            foreach (var module in modules)
            {
                // Match by name (case-insensitive), with or without .exe/.dll extension
                string modNameLower = module.ModuleName.ToLowerInvariant();
                string searchLower = moduleName.ToLowerInvariant();

                if (modNameLower == searchLower ||
                    modNameLower == searchLower + ".exe" ||
                    modNameLower == searchLower + ".dll" ||
                    modNameLower.Replace(".exe", "") == searchLower ||
                    modNameLower.Replace(".dll", "") == searchLower)
                {
                    // Found the module - calculate absolute address
                    long baseAddress = module.BaseAddress.ToInt64();
                    return baseAddress + offset;
                }
            }

            // Module not found
            return 0;
        }
    }
}
