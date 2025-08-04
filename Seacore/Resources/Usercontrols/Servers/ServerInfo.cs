using System.ComponentModel;

namespace Seacore.Resources.Usercontrols.Servers
{
    public class ServerInfo : INotifyPropertyChanged
    {
        private string name;
        private string port;
        private string status;
        private string uptime;

        public string Name
        {
            get => name;
            set
            {
                name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Port
        {
            get => port;
            set
            {
                port = value;
                OnPropertyChanged(nameof(Port));
            }
        }

        public string Status
        {
            get => status;
            set
            {
                status = value;
                OnPropertyChanged(nameof(Status));
            }
        }

        public string Uptime
        {
            get => uptime;
            set
            {
                uptime = value;
                OnPropertyChanged(nameof(Uptime));
            }
        }

        public DateTime StartTime { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ServerInfo(string name, string port, string status, string uptime)
        {
            Name = name;
            Port = port;
            Status = status;
            Uptime = uptime;
        }
    }
}
