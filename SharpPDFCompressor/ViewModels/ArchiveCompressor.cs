using SharpCompress.Common;
using SharpCompress.Archives;
using SharpPDFCompressor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPDFCompressor.ViewModels;

public class ArchiveCompressor : Compressor
{
    private string? _tempFolderLocation;

    /// <summary>
    /// Filters files for archive inclusion: for PDFs, only keeps the compressed version
    /// (containing the compressed suffix) if it exists; non-PDF files are always included.
    /// </summary>
    private IEnumerable<string> FilterFilesForArchive(string directoryPath)
    {
        List<string> allFiles = Directory.EnumerateFiles(AppUtils.NormalizePath(directoryPath), "*.*", SearchOption.AllDirectories)
            .ToList();
        List<string> result = new();

        foreach (string file in allFiles)
        {
            if (IsPdf(file))
            {
                if (!this.IgnoreCompressedFiles ||
                    Path.GetFileNameWithoutExtension(file)
                        .Contains(this.CompressedSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(file);
                }
            }
            else
            {
                result.Add(file);
            }
        }

        return result;
    }

    protected override async Task<CompressionResult> PreCompressAsync()
    {
        CompressionResult result = new();
        this.BeforeSizeBytes = new FileInfo(AppUtils.NormalizePath(this.InputFilesPath)).Length;
        string? parentDir = Path.GetDirectoryName(this.InputFilesPath);
        if (parentDir is null)
        {
            result.Errors.Add(this.LocalizedResources.GetLocalizedString("GenericError"));
            return result;
        }

        string zipExtractionDir = Path.Combine(parentDir, Path.GetRandomFileName());

        if (!Directory.Exists(AppUtils.NormalizePath(zipExtractionDir)))
        {
            Directory.CreateDirectory(AppUtils.NormalizePath(zipExtractionDir));
        }

        long totalDecompressedBytes = 0;
        long maxDecompressedBytes = (long)this.MaxDecompressedSizeMb * 1024L * 1024L;
        string resolvedExtractionDir = Path.GetFullPath(zipExtractionDir);

        try
        {
            this.ProgressHandler?.Report((
                null,
                null,
                this.LocalizedResources.GetLocalizedString("ExtractingStatus"),
                null,
                null));

            await using var archive = await ArchiveFactory.OpenAsyncArchive(AppUtils.NormalizePath(this.InputFilesPath), cancellationToken: this.Ct);

            await foreach (IArchiveEntry entry in archive.EntriesAsync)
            {
                this.Ct.ThrowIfCancellationRequested();

                if (entry.IsDirectory)
                {
                    string dirKey = entry.Key ?? string.Empty;
                    string destDirPath = Path.Combine(zipExtractionDir, dirKey.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(AppUtils.NormalizePath(destDirPath));
                    continue;
                }

                string entryKey = entry.Key ?? string.Empty;

                // Zip slip protection: verify the resolved destination stays inside the extraction directory
                string destPath = Path.Combine(zipExtractionDir, entryKey.Replace('/', Path.DirectorySeparatorChar));
                string resolvedDestPath = Path.GetFullPath(destPath);
                if (!resolvedDestPath.StartsWith(resolvedExtractionDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && resolvedDestPath != resolvedExtractionDir)
                {
                    result.Errors.Add(string.Format(
                        CultureInfo.CurrentCulture,
                        this.LocalizedResources.GetLocalizedString("ZipSlipError"),
                        entryKey));
                    return result;
                }

                // Zip bomb protection: check accumulated decompressed size before writing
                long entryLength = entry.Size;
                if (totalDecompressedBytes + entryLength > maxDecompressedBytes)
                {
                    result.Errors.Add(string.Format(
                        CultureInfo.CurrentCulture,
                        this.LocalizedResources.GetLocalizedString("ZipBombError"),
                        this.MaxDecompressedSizeMb));
                    return result;
                }

                string? destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(AppUtils.NormalizePath(destDir)))
                {
                    Directory.CreateDirectory(AppUtils.NormalizePath(destDir));
                }

                await using var fileStream = new FileStream(
                    AppUtils.NormalizePath(destPath),
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);

                await using var entryStream = await entry.OpenEntryStreamAsync(this.Ct);
                await entryStream.CopyToAsync(fileStream, this.Ct);

                // Track the actual decompressed size
                totalDecompressedBytes += new FileInfo(AppUtils.NormalizePath(destPath)).Length;
            }
        }
        catch (OperationCanceledException e)
        {
            result.Errors.Add(e.Message);
            return result;
        }
        catch (Exception e)
        {
#if DEBUG
            Console.Error.WriteLine(e);
#endif
            result.Errors.Add("Error occured " + e.Message);
            return result;
        }
        finally
        {
            // Clean up temp directory if extraction failed (result has errors and _tempFolderLocation is not yet set)
            if (result.Errors.Count > 0 && Directory.Exists(AppUtils.NormalizePath(zipExtractionDir)))
            {
                Directory.Delete(AppUtils.NormalizePath(zipExtractionDir), true);
            }
        }

        this._tempFolderLocation = zipExtractionDir;

        this.Files =
        [
            .. Directory.GetFiles(AppUtils.NormalizePath(zipExtractionDir), "*.*", SearchOption.AllDirectories)
                .Where(file => IsPdf(file) && (!this.IgnoreCompressedFiles ||
                                               !Path.GetFileNameWithoutExtension(file).Contains(this.CompressedSuffix,
                                                   StringComparison.OrdinalIgnoreCase)))
        ];

        this.PdfFilesCount = this.Files.Count();

        return result;
    }

    protected override CompressionResult PostFileCompress(string originalFilePath, string compressedFilePath)
    {
        return new CompressionResult();
    }

    protected override async Task<CompressionResult> PostCompressionAsync()
    {
        CompressionResult result = new();

        if (this._tempFolderLocation is null)
        {
            return result;
        }

        IEnumerable<string> files = this.FilterFilesForArchive(this._tempFolderLocation);

        try
        {
            string? resultDir = Path.GetDirectoryName(this.InputFilesPath);
            if (resultDir == null)
            {
                result.Errors.Add(this.LocalizedResources.GetLocalizedString("GenericError"));
                return result;
            }

            string originalArchiveName = Path.GetFileNameWithoutExtension(this.InputFilesPath);
            string archiveOutputPath = Path.Combine(resultDir, $"{originalArchiveName}.zip");
            string safeArchiveName = AppUtils.GetSafeFileName(archiveOutputPath, this.CompressedSuffix);
            string destName = Path.Combine(resultDir, safeArchiveName);
            int counter = 1;
            while (File.Exists(AppUtils.NormalizePath(destName)))
            {
                safeArchiveName = AppUtils.GetSafeFileName(archiveOutputPath, $"{this.CompressedSuffix} ({counter})");
                destName = Path.Combine(resultDir, safeArchiveName);
                counter++;
            }

            await using FileStream stream = new(
                AppUtils.NormalizePath(destName),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            // SharpCompress IWriter treats trailing-slash names as 0-byte files, which
            // shows up as a blank file next to the real folder. ZipArchive directory
            // entries preserve empty folders without that side effect.
            this.ProgressHandler?.Report((
                null,
                null,
                this.LocalizedResources.GetLocalizedString("WritingArchiveStatus"),
                null,
                null));

            using ZipArchive zipArchive = new(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);

            string[] allDirectories = [.. Directory.EnumerateDirectories(AppUtils.NormalizePath(this._tempFolderLocation), "*", SearchOption.AllDirectories)];
            foreach (string directory in allDirectories)
            {
                this.Ct.ThrowIfCancellationRequested();
                string relativePath = Path.GetRelativePath(this._tempFolderLocation, directory)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                zipArchive.CreateEntry(relativePath.TrimEnd('/') + "/");
            }

            foreach (string file in files)
            {
                this.Ct.ThrowIfCancellationRequested();

                string entryName = Path.GetRelativePath(this._tempFolderLocation, file)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');

                zipArchive.CreateEntryFromFile(AppUtils.NormalizePath(file), entryName, CompressionLevel.Optimal);
            }

            this.AfterSizeBytes = new FileInfo(AppUtils.NormalizePath(destName)).Length;
        }
        catch (Exception e)
        {
#if DEBUG
            Console.Error.WriteLine(e);
#endif
            result.Errors.Add(e.Message);
        }
        finally
        {
            if (Directory.Exists(AppUtils.NormalizePath(this._tempFolderLocation)))
            {
                Directory.Delete(AppUtils.NormalizePath(this._tempFolderLocation), true);
            }
        }

        return result;
    }
}