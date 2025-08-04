using System.Globalization;
using System.Windows.Data;

namespace Seacore.Resources.Usercontrols.Servers
{
    public class StatusToActionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                return status == "Listening" ? "Inactivate" : "Listen";
            }

            return "Listen";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}