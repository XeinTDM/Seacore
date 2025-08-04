using System.IO.Compression;

namespace SeacoreClient.Features.Recovery.Utilities
{
    internal static class UtilitiesCommon
    {
        private static readonly string RecoveryFolderPath = Path.Combine(Path.GetTempPath(), "Recovery");

        internal static void InitializeRecoveryFolder()
        {
            if (!Directory.Exists(RecoveryFolderPath))
            {
                Directory.CreateDirectory(RecoveryFolderPath);
            }
        }

        internal static string GetDateTimeFolder()
        {
            return DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
        }

        internal static void CompressRecoveryFolder()
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string zipFilePath = Path.Combine(desktopPath, "RecoveryFiles.zip");

            string tempDirPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirPath);

            string tempRecoveryDirPath = Path.Combine(tempDirPath, "Recovery");
            Directory.Move(RecoveryFolderPath, tempRecoveryDirPath);

            ZipFile.CreateFromDirectory(tempDirPath, zipFilePath);

            Directory.Move(tempRecoveryDirPath, RecoveryFolderPath);
            Directory.Delete(tempDirPath);
        }

        internal static string PrepareRecoveryFilePath(string subFolderName, string fileName)
        {
            string recoverySubFolderPath = Path.Combine(RecoveryFolderPath, subFolderName, GetDateTimeFolder());

            if (!Directory.Exists(recoverySubFolderPath))
            {
                Directory.CreateDirectory(recoverySubFolderPath);
            }

            return Path.Combine(recoverySubFolderPath, fileName);
        }
    }
}