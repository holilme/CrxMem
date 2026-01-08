using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CrxMem.Core
{
    /// <summary>
    /// Manages user comments on addresses with persistence to JSON file
    /// </summary>
    public static class CommentManager
    {
        private static Dictionary<long, string> _comments = new();
        private static readonly string _saveFolder;
        private static readonly string _saveFile;

        public static event Action? CommentsChanged;

        static CommentManager()
        {
            _saveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrxMem");
            _saveFile = Path.Combine(_saveFolder, "comments.json");
            Load();
        }

        /// <summary>
        /// Set a comment for an address
        /// </summary>
        public static void Set(long address, string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
            {
                Remove(address);
                return;
            }

            _comments[address] = comment;
            Save();
            CommentsChanged?.Invoke();
        }

        /// <summary>
        /// Get a comment for an address
        /// </summary>
        public static string? Get(long address)
        {
            return _comments.TryGetValue(address, out var comment) ? comment : null;
        }

        /// <summary>
        /// Check if an address has a comment
        /// </summary>
        public static bool HasComment(long address) => _comments.ContainsKey(address);

        /// <summary>
        /// Remove a comment for an address
        /// </summary>
        public static void Remove(long address)
        {
            if (_comments.Remove(address))
            {
                Save();
                CommentsChanged?.Invoke();
            }
        }

        /// <summary>
        /// Get all comments
        /// </summary>
        public static IReadOnlyDictionary<long, string> GetAll() => _comments;

        /// <summary>
        /// Save comments to JSON file
        /// </summary>
        public static void Save()
        {
            try
            {
                if (!Directory.Exists(_saveFolder))
                    Directory.CreateDirectory(_saveFolder);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_comments, options);
                File.WriteAllText(_saveFile, json);
            }
            catch
            {
                // Ignore save errors
            }
        }

        /// <summary>
        /// Load comments from JSON file
        /// </summary>
        public static void Load()
        {
            try
            {
                if (File.Exists(_saveFile))
                {
                    string json = File.ReadAllText(_saveFile);
                    var loaded = JsonSerializer.Deserialize<Dictionary<long, string>>(json);
                    if (loaded != null)
                    {
                        _comments = loaded;
                    }
                }
            }
            catch
            {
                _comments = new Dictionary<long, string>();
            }
        }

        /// <summary>
        /// Clear all comments
        /// </summary>
        public static void Clear()
        {
            _comments.Clear();
            Save();
            CommentsChanged?.Invoke();
        }
    }
}
