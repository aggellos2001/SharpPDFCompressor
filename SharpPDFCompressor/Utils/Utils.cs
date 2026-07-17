using System;
using System.IO;
using Windows.Storage;

namespace SharpPDFCompressor.Utils
{
    public class AppUtils
    {
        public static void GetTempDir(out string tempRootPath)
        {
            try
            {
                tempRootPath = ApplicationData.Current.TemporaryFolder.Path;
            }
            catch (InvalidOperationException)
            {
                tempRootPath = Path.GetTempPath();
            }
        }
    }
}