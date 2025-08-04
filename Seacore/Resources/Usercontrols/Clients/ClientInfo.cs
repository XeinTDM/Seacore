using System.ComponentModel;
using System.Net.Sockets;

namespace Seacore.Resources.Usercontrols.Clients
{
    public class ClientInfo : INotifyPropertyChanged
    {
        private double _ping;
        private string _location;
        private string _locationImg;
        private string _publicIP;

        public TcpClient TcpClient { get; }
        public string PublicIP
        {
            get => _publicIP;
            set
            {
                if (_publicIP != value)
                {
                    _publicIP = value;
                    OnPropertyChanged(nameof(PublicIP));
                }
            }
        }
        public string Username { get; set; }
        public string OS { get; set; }
        private string _osImg;
        public string OSImg
        {
            get => _osImg;
            set
            {
                if (_osImg != value)
                {
                    _osImg = value;
                    OnPropertyChanged(nameof(OSImg));
                }
            }
        }
        public string Version { get; set; }
        public DateTime Created { get; set; }

        public double Ping
        {
            get => _ping;
            set
            {
                if (_ping != value)
                {
                    _ping = value;
                    OnPropertyChanged(nameof(Ping));
                }
            }
        }

        public string Location
        {
            get => _location;
            set
            {
                if (_location != value)
                {
                    _location = value;
                    OnPropertyChanged(nameof(Location));
                }
            }
        }

        public string LocationImg
        {
            get => _locationImg;
            set
            {
                if (_locationImg != value)
                {
                    _locationImg = value;
                    OnPropertyChanged(nameof(LocationImg));
                }
            }
        }

        public ClientInfo(TcpClient client)
        {
            TcpClient = client;
            Created = DateTime.UtcNow;
            PublicIP = "Unknown";
            Location = "Unknown";
            LocationImg = "";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
