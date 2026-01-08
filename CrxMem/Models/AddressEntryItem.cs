using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrxMem.Models
{
    public class AddressEntryItem : INotifyPropertyChanged
    {
        private bool _frozen = false;
        private string _description = "";
        private string _address = "";
        private string _type = "";
        private string _value = "";
        private System.Windows.Media.Brush _addressForeground = System.Windows.Media.Brushes.White;

        public bool Frozen
        {
            get => _frozen;
            set { _frozen = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public string Address
        {
            get => _address;
            set { _address = value; OnPropertyChanged(); }
        }

        public string Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        public System.Windows.Media.Brush AddressForeground
        {
            get => _addressForeground;
            set { _addressForeground = value; OnPropertyChanged(); }
        }

        // Original AddressEntry properties
        public IntPtr AddressPtr { get; set; }
        public bool Active { get; set; } = true;
        public string FrozenValue { get; set; } = "";
        public bool ShowAsHex { get; set; } = false;
        public string OriginalAddressString { get; set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
