using SharpCompress.Archives;
using SharpPDFCompressor.Utils;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace SharpPDFCompressor.ViewModels;

public class ArchiveCompressor : Compressor
{
    private const int BufferSize = 81920;

    // Formats that are already compressed: deflating them again only burns CPU for ~0% gain.
    private static readonly HashSet<string> AlreadyCompressedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".webp",
        ".heic",
        ".avif",
        ".zip",
        ".7z",
        ".rar",
        ".gz",
        ".bz2",
        ".xz",
        ".zst",
        ".mp3",
        ".mp4",
        ".mkv",
        ".avi",
        ".mov",
        ".flac",
        ".ogg",
        ".docx",
        ".xlsx",
        ".pptx",
        ".odt",
        ".ods",
        ".odp"
    };

    // Relative paths (inside the temp folder) of extracted PDFs whose compressed version already exists.
    private readonly ConcurrentDictionary<string, byte> _compressedOriginals = new(StringComparer.OrdinalIgnoreCase);

    private string? _tempFolderLocation;
    private string? _resolvedTempRoot;
    private string? _outputZipPath;
    private FileStream? _zipStream;
    private ZipArchive? _zipArchive;
    private bool _completed;

    protected override bool IncludeCompressedPdfSizesInSummary => false;

    private bool ShouldCompress(string entryName)
    {
        return IsPdf(entryName) && (!this.IgnoreCompressedFiles ||
                                    !Path.GetFileNameWithoutExtension(entryName)
                                        .Contains(this.CompressedSuffix, StringComparison.OrdinalIgnoreCase));
    }

    private static CompressionLevel GetCompressionLevel(string entryName)
    {
        if (IsPdf(entryName))
        {
            return CompressionLevel.Fastest;
        }

        return AlreadyCompressedExtensions.Contains(Path.GetExtension(entryName))
            ? CompressionLevel.NoCompression
            : CompressionLevel.Optimal;
    }

    private static void TrySetLastWriteTime(ZipArchiveEntry entry, DateTime? lastWriteTime)
    {
        if (lastWriteTime is not { } time)
        {
            return;
        }

        try
        {
            entry.LastWriteTime = new DateTimeOffset(time);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or ArgumentException)
        {
            // Zip only supports 1980-2107; keep the default timestamp for anything outside that range.
        }
    }

    /// <summary>
    ///     Copies <paramref name="source" /> to <paramref name="destination" /> while counting the bytes that are
    ///     really produced. Returns -1 as soon as more than <paramref name="maxBytes" /> would be written.
    /// </summary>
    private async Task<long> CopyWithLimitAsync(Stream source, Stream destination, long maxBytes)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), this.Ct)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    return -1;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), this.Ct);
            }

            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private string CreateOutputZipPath(string resultDir)
    {
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

        return destName;
    }

    /// <summary>
    ///     Single pass over the source archive: PDFs that must be compressed are extracted to a temp folder
    ///     (MuPDF needs real files), everything else is streamed straight into the output zip.
    /// </summary>
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
        string resolvedExtractionDir = Path.GetFullPath(zipExtractionDir);
        // Set up front so the cleanup step removes it on every exit path. The folder itself is created lazily.
        this._tempFolderLocation = zipExtractionDir;
        this._resolvedTempRoot = resolvedExtractionDir;

        long totalDecompressedBytes = 0;
        long maxDecompressedBytes = (long)this.MaxDecompressedSizeMb * 1024L * 1024L;
        List<string> extractedPdfs = [];

        try
        {
            this.ProgressHandler?.Report((
                null,
                null,
                this.LocalizedResources.GetLocalizedString("ExtractingStatus"),
                null,
                null));

            this._outputZipPath = this.CreateOutputZipPath(parentDir);
            this._zipStream = new FileStream(
                AppUtils.NormalizePath(this._outputZipPath),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                true);

            // SharpCompress IWriter treats trailing-slash names as 0-byte files, which
            // shows up as a blank file next to the real folder. ZipArchive directory
            // entries preserve empty folders without that side effect.
            this._zipArchive = new ZipArchive(this._zipStream, ZipArchiveMode.Create, false, Encoding.UTF8);

            await using IAsyncArchive archive = await ArchiveFactory.OpenAsyncArchive(
                AppUtils.NormalizePath(this.InputFilesPath),
                cancellationToken: this.Ct);

            await foreach (IArchiveEntry entry in archive.EntriesAsync)
            {
                this.Ct.ThrowIfCancellationRequested();

                string entryKey = entry.Key ?? string.Empty;

                // Zip slip protection: verify the resolved destination stays inside the extraction directory
                string destPath = Path.Combine(zipExtractionDir, entryKey.Replace('/', Path.DirectorySeparatorChar));
                string resolvedDestPath = Path.GetFullPath(destPath);
                if (!resolvedDestPath.StartsWith(resolvedExtractionDir + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal)
                    && resolvedDestPath != resolvedExtractionDir)
                {
                    result.Errors.Add(string.Format(
                        CultureInfo.CurrentCulture,
                        this.LocalizedResources.GetLocalizedString("ZipSlipError"),
                        entryKey));
                    return result;
                }

                string entryName = Path.GetRelativePath(resolvedExtractionDir, resolvedDestPath)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                if (entryName == ".")
                {
                    continue;
                }

                if (entry.IsDirectory)
                {
                    ZipArchiveEntry directoryEntry = this._zipArchive.CreateEntry(entryName.TrimEnd('/') + "/");
                    TrySetLastWriteTime(directoryEntry, entry.LastModifiedTime);
                    continue;
                }

                // Zip bomb protection (early check on the declared size, exact check while copying below)
                long remainingBytes = maxDecompressedBytes - totalDecompressedBytes;
                if (entry.Size > remainingBytes)
                {
                    return this.ZipBombResult(result);
                }

                long written;
                await using Stream entryStream = await entry.OpenEntryStreamAsync(this.Ct);
                if (this.ShouldCompress(entryName))
                {
                    string? destDir = Path.GetDirectoryName(resolvedDestPath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(AppUtils.NormalizePath(destDir));
                    }

                    await using (FileStream fileStream = new(
                                     AppUtils.NormalizePath(resolvedDestPath),
                                     FileMode.Create,
                                     FileAccess.Write,
                                     FileShare.None,
                                     BufferSize,
                                     true))
                    {
                        written = await this.CopyWithLimitAsync(entryStream, fileStream, remainingBytes);
                    }

                    extractedPdfs.Add(resolvedDestPath);
                }
                else
                {
                    ZipArchiveEntry zipEntry = this._zipArchive.CreateEntry(entryName, GetCompressionLevel(entryName));
                    TrySetLastWriteTime(zipEntry, entry.LastModifiedTime);
                    await using Stream zipEntryStream = zipEntry.Open();
                    written = await this.CopyWithLimitAsync(entryStream, zipEntryStream, remainingBytes);
                }

                if (written < 0)
                {
                    return this.ZipBombResult(result);
                }

                totalDecompressedBytes += written;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
#if DEBUG
            Console.Error.WriteLine(e);
#endif
            result.Errors.Add("Error occured " + e.Message);
            return result;
        }

        this.Files = extractedPdfs;
        this.PdfFilesCount = extractedPdfs.Count;

        return result;
    }

    private CompressionResult ZipBombResult(CompressionResult result)
    {
        result.Errors.Add(string.Format(
            CultureInfo.CurrentCulture,
            this.LocalizedResources.GetLocalizedString("ZipBombError"),
            this.MaxDecompressedSizeMb));
        return result;
    }

    protected override CompressionResult PostFileCompress(string originalFilePath, string compressedFilePath)
    {
        // The extracted original is no longer needed: dropping it right away keeps peak disk usage low.
        this._compressedOriginals[Path.GetRelativePath(this._resolvedTempRoot!, originalFilePath)] = 0;
        TryDeleteFile(originalFilePath);
        return new CompressionResult();
    }

    protected override async Task<CompressionResult> PostCompressionAsync()
    {
        CompressionResult result = new();

        if (this._zipArchive is null || this._outputZipPath is null || this._tempFolderLocation is null)
        {
            result.Errors.Add(this.LocalizedResources.GetLocalizedString("GenericError"));
            return result;
        }

        try
        {
            this.ProgressHandler?.Report((
                null,
                null,
                this.LocalizedResources.GetLocalizedString("WritingArchiveStatus"),
                null,
                null));

            // Whatever is left in the temp folder is the compressed PDFs (and the originals of PDFs that failed).
            string normalizedTempRoot = AppUtils.NormalizePath(this._tempFolderLocation);
            if (Directory.Exists(normalizedTempRoot))
            {
                foreach (string file in Directory.EnumerateFiles(normalizedTempRoot, "*", SearchOption.AllDirectories))
                {
                    this.Ct.ThrowIfCancellationRequested();

                    string relativePath = Path.GetRelativePath(normalizedTempRoot, file);
                    if (this._compressedOriginals.ContainsKey(relativePath))
                    {
                        continue;
                    }

                    string entryName = relativePath
                        .Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');

                    ZipArchiveEntry zipEntry = this._zipArchive.CreateEntry(entryName, GetCompressionLevel(entryName));
                    TrySetLastWriteTime(zipEntry, File.GetLastWriteTime(file));

                    await using (Stream zipEntryStream = zipEntry.Open())
                    await using (FileStream fileStream = new(
                                     file,
                                     FileMode.Open,
                                     FileAccess.Read,
                                     FileShare.Read,
                                     BufferSize,
                                     FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await fileStream.CopyToAsync(zipEntryStream, BufferSize, this.Ct);
                    }

                    TryDeleteFile(file);
                }
            }

            // Disposing writes the central directory; only then is the file size final.
            await this._zipArchive.DisposeAsync();
            this._zipArchive = null;
            this._zipStream = null;

            this.AfterSizeBytes = new FileInfo(AppUtils.NormalizePath(this._outputZipPath)).Length;
            this._completed = true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
#if DEBUG
            Console.Error.WriteLine(e);
#endif
            result.Errors.Add(e.Message);
        }

        return result;
    }

    protected override async Task CleanupAfterCompressionAsync()
    {
        if (this._zipArchive is not null)
        {
            try
            {
                await this._zipArchive.DisposeAsync();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException
                                                  or NotSupportedException)
            {
                // The output is discarded below anyway.
            }

            this._zipArchive = null;
        }

        if (this._zipStream is not null)
        {
            await this._zipStream.DisposeAsync();
            this._zipStream = null;
        }

        // A cancelled or failed run must not leave a partial zip next to the source archive.
        if (!this._completed && this._outputZipPath is not null)
        {
            TryDeleteFile(this._outputZipPath);
        }

        if (this._tempFolderLocation is not null)
        {
            TryDeleteDirectory(this._tempFolderLocation);
        }
    }
}