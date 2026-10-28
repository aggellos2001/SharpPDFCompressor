using MuPDF.NET;
using SharpPDFCompressor.DataModels;
using SharpPDFCompressor.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinUI3Localizer;
using FileInfo = System.IO.FileInfo;

namespace SharpPDFCompressor.ViewModels;

public class CompressionResult
{
    public List<string> Errors { get; init; } = [];
    public bool HasErrors => this.Errors.Count > 0;
    public long? BeforeSizeBytes { get; set; }
    public long? AfterSizeBytes { get; set; }
}

public abstract class Compressor
{
    public const string DefaultCompressedSuffix = "_compressed";

    //private variables set by the compressor only
    protected readonly ILocalizer LocalizedResources = Localizer.Get();

    protected IEnumerable<string> Files { get; set; } = [];
    protected int PdfFilesCount { get; set; }
    protected long? BeforeSizeBytes { get; set; }
    protected long? AfterSizeBytes { get; set; }


    // public variables required to be set.
    public bool DeleteOriginalFiles { get; init; }
    public bool CompressImages { get; init; }
    public required string CompressedSuffix { get; init; }
    public bool IgnoreCompressedFiles { get; init; }
    public int MaxDecompressedSizeMb { get; init; } = AppSettings.DefaultMaxDecompressedSizeMb;
    public required CancellationToken Ct { get; init; }

    public required
        IProgress<(double? progressValue, int? workerID, string? message, int? completedFiles, int? totalFiles)>?
        ProgressHandler { get; init; }

    public required string InputFilesPath { get; init; }
    public int NumOfThreads { get; init; } = 4;


    /// <summary>
    ///     Runs the whole compression pipeline on a thread-pool thread so that none of the synchronous
    ///     file system or CPU heavy work can run on (and block) the UI thread. Never throws: cancellation
    ///     yields an empty result and any other failure is reported through <see cref="CompressionResult.Errors" />.
    /// </summary>
    public Task<CompressionResult> ExecuteCompressAsync()
    {
        return Task.Run(this.ExecuteCompressCoreAsync);
    }

    private async Task<CompressionResult> ExecuteCompressCoreAsync()
    {
        try
        {
            CompressionResult result = await this.PreCompressAsync();
            this.Ct.ThrowIfCancellationRequested();
            if (result.HasErrors)
            {
                return this.AddSizeSummary(result);
            }

            CompressionResult compressionResult = await this.CompressAsync();
            this.Ct.ThrowIfCancellationRequested();

            result = await this.PostCompressionAsync();
            result.Errors.AddRange(compressionResult.Errors);
            return this.AddSizeSummary(result);
        }
        catch (Exception) when (this.Ct.IsCancellationRequested)
        {
            // Cancelled by the user: no summary and no errors, cleanup happens in the finally block.
            return new CompressionResult();
        }
        catch (Exception exception)
        {
#if DEBUG
            Console.Error.WriteLine(exception);
#endif
            return new CompressionResult { Errors = [exception.Message] };
        }
        finally
        {
            try
            {
                await this.CleanupAfterCompressionAsync();
            }
            catch (Exception exception)
            {
#if DEBUG
                Console.Error.WriteLine(exception);
#endif
            }
        }
    }

    /// <summary>
    ///     Always called (success, failure and cancellation) after the pipeline finished.
    ///     Implementations must not rely on the cancellation token still being usable.
    /// </summary>
    protected virtual Task CleanupAfterCompressionAsync()
    {
        return Task.CompletedTask;
    }

    protected static void TryDeleteFile(string path)
    {
        try
        {
            string normalizedPath = AppUtils.NormalizePath(path);
            if (File.Exists(normalizedPath))
            {
                File.Delete(normalizedPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
#if DEBUG
            Console.Error.WriteLine(exception);
#endif
        }
    }

    protected static void TryDeleteDirectory(string path)
    {
        try
        {
            string normalizedPath = AppUtils.NormalizePath(path);
            if (Directory.Exists(normalizedPath))
            {
                Directory.Delete(normalizedPath, true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
#if DEBUG
            Console.Error.WriteLine(exception);
#endif
        }
    }

    private static long GetFileLengthOrZero(string file)
    {
        try
        {
            return new FileInfo(AppUtils.NormalizePath(file)).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private CompressionResult AddSizeSummary(CompressionResult result)
    {
        result.BeforeSizeBytes = this.BeforeSizeBytes;
        result.AfterSizeBytes = this.AfterSizeBytes;
        return result;
    }

    protected abstract Task<CompressionResult> PreCompressAsync();
    protected abstract CompressionResult PostFileCompress(string originalFilePath, string compressedFilePath);

    protected virtual string GetCompressedOutputPath(string inputFilePath, string compressedFileName)
    {
        string? directoryName = Path.GetDirectoryName(inputFilePath);
        return Path.Combine(directoryName!, compressedFileName);
    }

    protected virtual bool IncludeCompressedPdfSizesInSummary => true;

    /// <summary>
    ///     Claims an output path before it is written. Returns false when the path is already claimed by
    ///     something else that is being written concurrently, so another name has to be chosen.
    /// </summary>
    protected virtual bool TryReserveOutputPath(string outputPath)
    {
        return true;
    }

    protected virtual void OnFileCompressFailed(string file)
    {
    }

    protected abstract Task<CompressionResult> PostCompressionAsync();

    protected static bool IsPdf(string file)
    {
        return Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    protected static long GetFilesSize(IEnumerable<string> files)
    {
        return files.Sum(file => new FileInfo(AppUtils.NormalizePath(file)).Length);
    }


    private async Task<CompressionResult> CompressAsync()
    {
        int degreeOfParallelism = Math.Max(1, this.NumOfThreads);
        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = degreeOfParallelism, CancellationToken = this.Ct
        };
        ConcurrentBag<string> threadErrors = [];
        // At most degreeOfParallelism bodies run at once, so a slot is always available.
        ConcurrentQueue<int> availableStatusSlots = new(Enumerable.Range(0, degreeOfParallelism));
        int completedFiles = 0;
        long afterSizeBytes = this.AfterSizeBytes ?? 0;

        await Task.Run(() =>
        {
            this.ProgressHandler?.Report((null, null, null, 0, this.PdfFilesCount));

            // Largest files first, so a huge file does not end up alone at the tail of the run.
            List<string> orderedFiles = [.. this.Files.OrderByDescending(GetFileLengthOrZero)];

            Parallel.ForEach(Partitioner.Create(orderedFiles, EnumerablePartitionerOptions.NoBuffering),
                parallelOptions, file =>
                {
                    if (this.Ct.IsCancellationRequested)
                    {
                        return;
                    }

                    if (!availableStatusSlots.TryDequeue(out int slotIndex))
                    {
                        slotIndex = 0;
                    }

                    string? pendingOutputPath = null;
                    bool cancelled = false;

                    try
                    {
                        string? directoryName = Path.GetDirectoryName(file);
                        if (directoryName == null)
                        {
                            threadErrors.Add($"{file}: {this.LocalizedResources.GetLocalizedString("GenericError")}");
                            return;
                        }

                        string safeFileName = AppUtils.GetSafeFileName(file, this.CompressedSuffix);
                        string compressedFilePath = this.GetCompressedOutputPath(file, safeFileName);
                        int counter = 1;
                        // Keep appending a counter until we find a filename that doesn't exist yet
                        while (File.Exists(AppUtils.NormalizePath(compressedFilePath)) ||
                               !this.TryReserveOutputPath(compressedFilePath))
                        {
                            safeFileName = AppUtils.GetSafeFileName(file, $"{this.CompressedSuffix} ({counter})");
                            compressedFilePath = this.GetCompressedOutputPath(file, safeFileName);
                            counter++;
                        }

                        pendingOutputPath = compressedFilePath;
                        this.ProgressHandler?.Report((
                            null, slotIndex, $"{this.LocalizedResources.GetLocalizedString("Compressing")} {file}",
                            null,
                            null));

                        this.Ct.ThrowIfCancellationRequested();
                        Document document = AppUtils.OpenDocumentForMuPDF(file);
                        try
                        {
                            string? compressedDirectory = Path.GetDirectoryName(compressedFilePath);
                            if (compressedDirectory == null)
                            {
                                threadErrors.Add(
                                    $"{file}: {this.LocalizedResources.GetLocalizedString("GenericError")}");
                                return;
                            }

                            Directory.CreateDirectory(AppUtils.NormalizePath(compressedDirectory));
                            if (this.CompressImages)
                            {
                                // Recompress images to reduce file size:
                                // - Resample images with DPI > 300 down to 150 DPI
                                // - Use JPEG quality 75 for lossy compression
                                // - Include all image types (lossy, lossless, bitonal, color, gray)
                                document.RewriteImages(
                                    dpiThreshold: 300,
                                    dpiTarget: 150,
                                    quality: 75,
                                    lossy: true,
                                    lossless: true,
                                    bitonal: true,
                                    color: true,
                                    gray: true
                                );
                            }

                            AppUtils.SaveDocumentForMuPDF(document, compressedFilePath, 4, 1, 1);
                        }
                        finally
                        {
                            document.Close();
                        }

                        this.Ct.ThrowIfCancellationRequested();

                        // Never make a file bigger: fall back to the original content when the result grew.
                        long originalLength = new FileInfo(AppUtils.NormalizePath(file)).Length;
                        long compressedLength = new FileInfo(AppUtils.NormalizePath(compressedFilePath)).Length;
                        if (compressedLength > originalLength)
                        {
                            File.Copy(AppUtils.NormalizePath(file), AppUtils.NormalizePath(compressedFilePath), true);
                            compressedLength = originalLength;
                        }

                        if (this.IncludeCompressedPdfSizesInSummary)
                        {
                            Interlocked.Add(ref afterSizeBytes, compressedLength);
                        }

                        // From here on the output belongs to the post-processing step and must not be deleted.
                        pendingOutputPath = null;
                        this.PostFileCompress(file, compressedFilePath);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }
                    catch (Exception exception)
                    {
#if DEBUG
                        Console.Error.WriteLine(exception);
#endif
                        threadErrors.Add($"{file}: {exception.Message}");
                        this.OnFileCompressFailed(file);
                    }
                    finally
                    {
                        if (pendingOutputPath is not null)
                        {
                            // Failed or cancelled before the output was handed over: do not leave partial files behind.
                            TryDeleteFile(pendingOutputPath);
                        }

                        if (!cancelled)
                        {
                            int completedFileCount = Interlocked.Increment(ref completedFiles);
                            this.ProgressHandler?.Report((
                                1.0 / this.PdfFilesCount * 100,
                                slotIndex,
                                "Done...",
                                completedFileCount,
                                this.PdfFilesCount));
                        }

                        availableStatusSlots.Enqueue(slotIndex);
                    }
                });
        }, this.Ct);

        if (this.IncludeCompressedPdfSizesInSummary)
        {
            this.AfterSizeBytes = afterSizeBytes;
        }

        return new CompressionResult { Errors = [.. threadErrors] };
    }
}