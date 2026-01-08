using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrxMem.Models
{
    public class ScanResultItem : INotifyPropertyChanged
    {
        private string _address = "";
        private string _value = "";
        private string _previous = "";
        private System.Windows.Media.Brush _foreground = System.Windows.Media.Brushes.White;

        public string Address
        {
            get => _address;
            set { _address = value; OnPropertyChanged(); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        public string Previous
        {
            get => _previous;
            set { _previous = value; OnPropertyChanged(); }
        }

        public System.Windows.Media.Brush Foreground
        {
            get => _foreground;
            set { _foreground = value; OnPropertyChanged(); }
        }

        // Store original ScanResult data
        public IntPtr AddressPtr { get; set; }
        public byte[] ValueBytes { get; set; } = Array.Empty<byte>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
