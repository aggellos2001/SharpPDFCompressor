using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.IO;
using Windows.Storage;

namespace SharpPDFCompressor.Utils
{
    public class AppUtils
    {
        private static readonly ResourceLoader ResourceLoader = new();

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

        public static string GetSafeFileName(string fullPath, string suffix)
        {
            const int maxPathLength = 255;
            string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            string extension = Path.GetExtension(fullPath);
            string fileName = Path.GetFileNameWithoutExtension(fullPath);
            int directoryLength = string.IsNullOrEmpty(directory) ? 0 : directory.Length + 1;

            int fixedLength = directoryLength + suffix.Length + extension.Length;
            int availableNameLength = maxPathLength - fixedLength;
            if (availableNameLength <= 0)
            {
                throw new PathTooLongException(ResourceLoader.GetString("LongNameException"));
            }

            if (fileName.Length > availableNameLength)
            {
                fileName = fileName[..availableNameLength];
            }

            string safeFileName = $"{fileName}{suffix}{extension}";
            return string.IsNullOrEmpty(directory) ? safeFileName : Path.Combine(directory, safeFileName);
        }
    }
}