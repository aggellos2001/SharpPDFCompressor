using SharpCompress.Common;
using SharpCompress.Archives;
using SharpCompress.Writers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        List<string> allFiles = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
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
        this.BeforeSizeBytes = new FileInfo(this.InputFilesPath).Length;
        string? parentDir = Path.GetDirectoryName(this.InputFilesPath);
        if (parentDir is null)
        {
            result.Errors.Add(this.LocalizedResources.GetLocalizedString("GenericError"));
            return result;
        }

        string zipExtractionDir = Path.Combine(parentDir, Path.GetRandomFileName());

        if (!Directory.Exists(zipExtractionDir))
        {
            Directory.CreateDirectory(zipExtractionDir);
        }

        long totalDecompressedBytes = 0;
        long maxDecompressedBytes = (long)this.MaxDecompressedSizeMb * 1024L * 1024L;
        string resolvedExtractionDir = Path.GetFullPath(zipExtractionDir);

        try
        {
            await using var archive = await ArchiveFactory.OpenAsyncArchive(this.InputFilesPath, cancellationToken: this.Ct);

            await foreach (var entry in archive.EntriesAsync)
            {
                this.Ct.ThrowIfCancellationRequested();

                if (entry.IsDirectory)
                {
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

                this.ProgressHandler?.Report((null, 0, $"Extracting: {entryKey}", null, null));

                string? destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                await using var fileStream = new FileStream(
                    destPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);

                await using var entryStream = await entry.OpenEntryStreamAsync(this.Ct);
                await entryStream.CopyToAsync(fileStream, this.Ct);

                // Track the actual decompressed size
                totalDecompressedBytes += new FileInfo(destPath).Length;
            }
        }
        catch (OperationCanceledException e)
        {
            result.Errors.Add(e.Message);
            return result;
        }
        catch (Exception e)
        {
            result.Errors.Add("Error occured " + e.Message);
            return result;
        }
        finally
        {
            // Clean up temp directory if extraction failed (result has errors and _tempFolderLocation is not yet set)
            if (result.Errors.Count > 0 && Directory.Exists(zipExtractionDir))
            {
                Directory.Delete(zipExtractionDir, true);
            }
        }

        this._tempFolderLocation = zipExtractionDir;

        this.Files =
        [
            .. Directory.GetFiles(zipExtractionDir, "*.*", SearchOption.AllDirectories)
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
            string? destName = Path.Combine(resultDir, $"{originalArchiveName}{this.CompressedSuffix}.zip");
            int counter = 1;
            while (File.Exists(destName))
            {
                destName = Path.Combine(resultDir, $"{originalArchiveName}{this.CompressedSuffix}({counter}).zip");
                counter++;
            }

            await using FileStream stream = new(
                destName,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            await using var writer = await WriterFactory.OpenAsyncWriter(
                stream, ArchiveType.Zip,
                new WriterOptions(CompressionType.Deflate)
                {
                    ArchiveEncoding = new ArchiveEncoding { Forced = Encoding.UTF8 }
                }, this.Ct);

            foreach (string file in files)
            {
                this.Ct.ThrowIfCancellationRequested();
                
                this.ProgressHandler?.Report((null, 0, $"Writing to archive: {Path.GetFileName(file)}", null, null));

                await using FileStream fileStream = new(
                    file,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    useAsync: true);
                
                string entryName = Path.GetRelativePath(this._tempFolderLocation, file)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                
                await writer.WriteAsync(entryName, fileStream, File.GetLastWriteTime(file), this.Ct);
            }

            this.AfterSizeBytes = new FileInfo(destName).Length;
        }
        catch (Exception e)
        {
            result.Errors.Add(e.Message);
        }
        finally
        {
            if (Directory.Exists(this._tempFolderLocation))
            {
                Directory.Delete(this._tempFolderLocation, true);
            }
        }

        return result;
    }
}