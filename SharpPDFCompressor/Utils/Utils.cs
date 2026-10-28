using MuPDF.NET;
using System;
using System.IO;
using System.Linq;
using Windows.Storage;
using WinUI3Localizer;

namespace SharpPDFCompressor.Utils;

public static class AppUtils
{
    private const int MaxPathLength = 260;
    private static readonly ILocalizer LocalizedResources = Localizer.Get();

    public static string NormalizePath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        // Fast path: resolving a fully qualified path can only shorten it, so no GetFullPath call is needed.
        if (path.Length <= MaxPathLength && Path.IsPathFullyQualified(path))
        {
            return path;
        }

        if (GetAbsolutePathLength(path) <= MaxPathLength)
        {
            return path;
        }

        string fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return fullPath;
        }

        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + fullPath[2..]
            : @"\\?\" + fullPath;
    }

    private static int GetAbsolutePathLength(string path)
    {
        return GetAbsolutePath(path).Length;
    }

    private static string GetAbsolutePath(string path)
    {
        string pathWithoutExtendedPrefix = path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
            ? @"\\" + path[8..]
            : path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
                ? path[4..]
                : path;

        return Path.GetFullPath(pathWithoutExtendedPrefix);
    }

    public static Document OpenDocumentForMuPDF(string filePath)
    {
        string absolutePath = GetAbsolutePath(filePath);
        bool containsNonAscii = absolutePath.Any(character => character > 127);
        int byteArrayThreshold = containsNonAscii ? 240 : MaxPathLength;
        return absolutePath.Length <= byteArrayThreshold
            ? new Document(filePath)
            : new Document(File.ReadAllBytes(NormalizePath(filePath)));
    }

    public static void SaveDocumentForMuPDF(Document document, string filePath, int flags, int deflate, int useObjstms)
    {
        if (GetAbsolutePathLength(filePath) <= MaxPathLength)
        {
            document.Save(filePath, flags, deflate: deflate, useObjstms: useObjstms);
            return;
        }

        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        if (tempPath.Length > MaxPathLength)
        {
            throw new PathTooLongException("The temporary path for saving the PDF exceeds 260 characters.");
        }

        try
        {
            document.Save(tempPath, flags, deflate: deflate, useObjstms: useObjstms);
            File.Move(NormalizePath(tempPath), NormalizePath(filePath));
        }
        finally
        {
            string normalizedTempPath = NormalizePath(tempPath);
            if (File.Exists(normalizedTempPath))
            {
                File.Delete(normalizedTempPath);
            }
        }
    }

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

    public static string GetSafeFileName(string fullPath, string suffix, int maxPathLimit = MaxPathLength)
    {
        string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        string extension = Path.GetExtension(fullPath);
        string fileName = Path.GetFileNameWithoutExtension(fullPath);
        int directoryLength = directory.Length;
        if (!string.IsNullOrEmpty(directory) &&
            directory[^1] != Path.DirectorySeparatorChar && directory[^1] != Path.AltDirectorySeparatorChar)
        {
            directoryLength++;
        }

        int fixedLength = directoryLength + suffix.Length + extension.Length;
        int availableNameLength = maxPathLimit - fixedLength;
        if (availableNameLength <= 0)
        {
            throw new PathTooLongException(LocalizedResources.GetLocalizedString("LongNameException") + "Filename:  " +
                                           fullPath);
        }

        if (fileName.Length > availableNameLength)
        {
            fileName = fileName[..availableNameLength];
        }

        return $"{fileName}{suffix}{extension}";
    }
}