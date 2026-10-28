using SharpPDFCompressor.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpPDFCompressor.ViewModels;

public class DirectoryCompressor : Compressor
{
    private const int CopyBufferSize = 81920;

    private readonly ConcurrentBag<string> _copyErrors = [];

    // Output paths that are taken: files that get copied under their original name and compressed outputs.
    private readonly ConcurrentDictionary<string, byte> _reservedOutputPaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _targetRootLocation;
    private Task<long>? _copyTask;
    private CancellationTokenSource? _copyCts;
    private bool _completed;

    protected override Task<CompressionResult> PreCompressAsync()
    {
        CompressionResult result = new();
        string sourceRoot = AppUtils.NormalizePath(this.InputFilesPath);

        this.ProgressHandler?.Report((
            null,
            null,
            this.LocalizedResources.GetLocalizedString("ScanningFilesStatus"),
            null,
            null));

        // Single enumeration: name, length and timestamp come from the directory listing itself.
        List<string> pdfFiles = [];
        List<(string Path, long Length, DateTime LastWriteUtc)> filesToCopy = [];
        long totalBytes = 0;
        long pdfBytes = 0;
        foreach (FileInfo file in new DirectoryInfo(sourceRoot).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            this.Ct.ThrowIfCancellationRequested();
            long length = file.Length;
            totalBytes += length;
            if (IsPdf(file.Name) && (!this.IgnoreCompressedFiles ||
                                     !Path.GetFileNameWithoutExtension(file.Name)
                                         .Contains(this.CompressedSuffix, StringComparison.OrdinalIgnoreCase)))
            {
                pdfFiles.Add(file.FullName);
                pdfBytes += length;
            }
            else
            {
                // Everything that is not compressed (including already compressed PDFs) is copied as is.
                filesToCopy.Add((file.FullName, length, file.LastWriteTimeUtc));
            }
        }

        this.Files = pdfFiles;
        this.PdfFilesCount = pdfFiles.Count;
        this.BeforeSizeBytes = this.DeleteOriginalFiles ? totalBytes : pdfBytes;

        if (!this.DeleteOriginalFiles)
        {
            return Task.FromResult(result);
        }

        string trimmedInputPath = this.InputFilesPath
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string targetRootDirectory = trimmedInputPath + this.CompressedSuffix;
        int counter = 1;
        while (Directory.Exists(AppUtils.NormalizePath(targetRootDirectory)))
        {
            this.Ct.ThrowIfCancellationRequested();
            targetRootDirectory = trimmedInputPath + $"{this.CompressedSuffix} ({counter++})";
        }

        this._targetRootLocation = targetRootDirectory;
        Directory.CreateDirectory(AppUtils.NormalizePath(targetRootDirectory));

        // Copy all directories (including empty ones) to preserve folder structure.
        foreach (string directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            this.Ct.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(sourceRoot, directory);
            string targetDirectory = Path.Combine(targetRootDirectory, relativePath);
            Directory.CreateDirectory(AppUtils.NormalizePath(targetDirectory));
        }

        // Copied files keep their original name, so compressed outputs must not claim those names.
        foreach ((string Path, long Length, DateTime LastWriteUtc) file in filesToCopy)
        {
            string relativeFilePath = Path.GetRelativePath(sourceRoot, file.Path);
            this._reservedOutputPaths.TryAdd(GetReservationKey(Path.Combine(targetRootDirectory, relativeFilePath)), 0);
        }

        // The copy runs in the background while the PDFs are being compressed and is awaited afterwards.
        this._copyCts = CancellationTokenSource.CreateLinkedTokenSource(this.Ct);
        this._copyTask = this.CopyFilesAsync(sourceRoot, targetRootDirectory, filesToCopy, this._copyCts.Token);

        return Task.FromResult(result);
    }

    private async Task<long> CopyFilesAsync(
        string sourceRoot,
        string targetRoot,
        List<(string Path, long Length, DateTime LastWriteUtc)> files,
        CancellationToken cancellationToken)
    {
        this.ProgressHandler?.Report((
            null,
            null,
            this.LocalizedResources.GetLocalizedString("CopyingFilesStatus"),
            null,
            null));

        long copiedBytes = 0;
        ParallelOptions options = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, this.NumOfThreads), CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(files, options, async (file, token) =>
        {
            string relativePath = Path.GetRelativePath(sourceRoot, file.Path);
            string targetFilePath = Path.Combine(targetRoot, relativePath);
            string normalizedTargetPath = AppUtils.NormalizePath(targetFilePath);
            bool targetCreated = false;

            try
            {
                string? targetDirectory = Path.GetDirectoryName(targetFilePath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(AppUtils.NormalizePath(targetDirectory));
                }

                long written;
                await using (FileStream source = new(
                                 AppUtils.NormalizePath(file.Path),
                                 FileMode.Open,
                                 FileAccess.Read,
                                 FileShare.Read,
                                 CopyBufferSize,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (FileStream target = new(
                                 normalizedTargetPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 CopyBufferSize,
                                 FileOptions.Asynchronous))
                {
                    targetCreated = true;
                    await source.CopyToAsync(target, CopyBufferSize, token);
                    written = target.Length;
                }

                File.SetLastWriteTimeUtc(normalizedTargetPath, file.LastWriteUtc);
                Interlocked.Add(ref copiedBytes, written);
            }
            catch (OperationCanceledException)
            {
                if (targetCreated)
                {
                    TryDeleteFile(targetFilePath);
                }

                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
#if DEBUG
                Console.Error.WriteLine(exception);
#endif
                if (targetCreated)
                {
                    TryDeleteFile(targetFilePath);
                }

                this._copyErrors.Add($"{file.Path}: {exception.Message}");
            }
        });

        return copiedBytes;
    }

    private static string GetReservationKey(string path)
    {
        return Path.GetFullPath(AppUtils.NormalizePath(path));
    }

    protected override bool TryReserveOutputPath(string outputPath)
    {
        return !this.DeleteOriginalFiles || this._reservedOutputPaths.TryAdd(GetReservationKey(outputPath), 0);
    }

    protected override string GetCompressedOutputPath(string inputFilePath, string compressedFileName)
    {
        if (!this.DeleteOriginalFiles)
        {
            return base.GetCompressedOutputPath(inputFilePath, compressedFileName);
        }

        string sourceRoot = AppUtils.NormalizePath(this.InputFilesPath);
        string relativePath = Path.GetRelativePath(sourceRoot, inputFilePath);
        string? relativeDirectory = Path.GetDirectoryName(relativePath);
        return Path.Combine(this._targetRootLocation!, relativeDirectory ?? string.Empty, compressedFileName);
    }

    protected override CompressionResult PostFileCompress(string originalFilePath, string compressedFilePath)
    {
        return new CompressionResult();
    }

    protected override async Task<CompressionResult> PostCompressionAsync()
    {
        CompressionResult result = new();

        if (this._copyTask is not null)
        {
            this.ProgressHandler?.Report((
                null,
                null,
                this.LocalizedResources.GetLocalizedString("CopyingFilesStatus"),
                null,
                null));

            long copiedBytes = await this._copyTask;
            this.AfterSizeBytes = (this.AfterSizeBytes ?? 0) + copiedBytes;
            result.Errors.AddRange(this._copyErrors);
        }

        this._completed = true;
        return result;
    }

    protected override async Task CleanupAfterCompressionAsync()
    {
        if (this._copyTask is not null)
        {
            if (!this._completed)
            {
                await this._copyCts!.CancelAsync();
            }

            try
            {
                await this._copyTask;
            }
            catch (Exception)
            {
                // Already reported through the main pipeline (or cancelled); we only need it to have stopped.
            }
        }

        this._copyCts?.Dispose();

        // A cancelled or failed run must not leave a half-filled output folder behind.
        if (!this._completed && this._targetRootLocation is not null)
        {
            TryDeleteDirectory(this._targetRootLocation);
        }
    }
}