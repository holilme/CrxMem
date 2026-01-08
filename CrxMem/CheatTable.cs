using System;
using System.Collections.Generic;

namespace CrxMem
{
    public class CheatTable
    {
        public string Version { get; set; } = "1.0";
        public string ProcessName { get; set; } = "";
        public List<CheatEntry> Entries { get; set; } = new();
    }

    public class CheatEntry
    {
        public string Address { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "4 Bytes";
        public bool Frozen { get; set; }
        public string FrozenValue { get; set; } = "";
        public bool Active { get; set; } = true;
        public bool ShowAsHex { get; set; } = false;
    }
}
