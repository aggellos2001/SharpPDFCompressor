using SharpPDFCompressor.DataModels;
using SharpPDFCompressor.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WinUI3Localizer;

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
    private BlockingCollection<int>? _availableStatusSlots;

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
    public required IProgress<(double? progressValue, int? workerID, string? message, int? completedFiles, int? totalFiles)>? ProgressHandler { get; init; }

    public required string InputFilesPath { get; init; }
    public int NumOfThreads { get; init; } = 4;


    public async Task<CompressionResult> ExecuteCompressAsync()
    {
        CompressionResult result = await this.PreCompressAsync();
        if (result.HasErrors || this.Ct.IsCancellationRequested)
        {
            return this.AddSizeSummary(result);
        }

        CompressionResult compressionResult = await this.CompressAsync();
        if (this.Ct.IsCancellationRequested)
        {
            return this.AddSizeSummary(compressionResult);
        }

        result = await this.PostCompressionAsync();
        result.Errors.AddRange(compressionResult.Errors);
        return this.AddSizeSummary(result);
    }

    private CompressionResult AddSizeSummary(CompressionResult result)
    {
        result.BeforeSizeBytes = this.BeforeSizeBytes;
        result.AfterSizeBytes = this.AfterSizeBytes;
        return result;
    }

    protected abstract Task<CompressionResult> PreCompressAsync();
    protected abstract CompressionResult PostFileCompress(string originalFilePath, string compressedFilePath);

    protected virtual void OnFileCompressFailed(string file)
    {
    }

    protected abstract Task<CompressionResult> PostCompressionAsync();

    protected static bool IsPdf(string file) => Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    protected static long GetFilesSize(IEnumerable<string> files)
    {
        long totalSize = 0;
        foreach (string file in files)
        {
            totalSize += new System.IO.FileInfo(AppUtils.NormalizePath(file)).Length;
        }

        return totalSize;
    }

    protected static long GetDirectorySize(string directoryPath) =>
        GetFilesSize(Directory.EnumerateFiles(AppUtils.NormalizePath(directoryPath), "*.*", SearchOption.AllDirectories));

    private async Task<CompressionResult> CompressAsync()
    {
        ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = this.NumOfThreads };
        ConcurrentBag<string> threadErrors = [];
        int completedFiles = 0;
        long afterSizeBytes = 0;

        await Task.Run(() =>
        {
            this.ProgressHandler?.Report((null, null, null, 0, this.PdfFilesCount));
            this._availableStatusSlots = new BlockingCollection<int>(new ConcurrentQueue<int>());
            for (int i = 0; i < Math.Max(1, this.NumOfThreads); i++)
            {
                this._availableStatusSlots.Add(i, this.Ct);
            }

            Parallel.ForEach([.. this.Files], parallelOptions, file =>
            {
                if (this.Ct.IsCancellationRequested)
                {
                    return;
                }

                int slotIndex = this._availableStatusSlots.Take(this.Ct);

                try
                {
                    string? directoryName = Path.GetDirectoryName(file);
                    if (directoryName == null)
                    {
                        threadErrors.Add($"{file}: {this.LocalizedResources.GetLocalizedString("GenericError")}");
                        return;
                    }

                    string safeFileName = AppUtils.GetSafeFileName(file, this.CompressedSuffix);
                    string compressedFileName = Path.Combine(directoryName, $"{safeFileName}");
                    int counter = 1;
                    // Keep appending a counter until we find a filename that doesn't exist yet
                    while (File.Exists(AppUtils.NormalizePath(compressedFileName)))
                    {
                        safeFileName = AppUtils.GetSafeFileName(file, $"{this.CompressedSuffix} ({counter})");
                        compressedFileName = Path.Combine(directoryName, safeFileName);
                        counter++;
                    }

                    this.ProgressHandler?.Report((
                        null, slotIndex, $"{this.LocalizedResources.GetLocalizedString("Compressing")} {file}", null, null));

                    this.Ct.ThrowIfCancellationRequested();
                    var document = AppUtils.OpenDocumentForMuPDF(file);
                    try
                    {
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

                        AppUtils.SaveDocumentForMuPDF(document, compressedFileName, 4, deflate: 1, useObjstms: 1);
                    }
                    finally
                    {
                        document.Close();
                    }

                    this.Ct.ThrowIfCancellationRequested();
                    Interlocked.Add(ref afterSizeBytes,
                        new System.IO.FileInfo(AppUtils.NormalizePath(compressedFileName)).Length);
                    this.PostFileCompress(file, compressedFileName);
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
                    int completedFileCount = Interlocked.Increment(ref completedFiles);
                    this.ProgressHandler?.Report((
                        1.0 / this.PdfFilesCount * 100,
                        slotIndex,
                        "Done...",
                        completedFileCount,
                        this.PdfFilesCount));
                    try
                    {
                        this._availableStatusSlots.Add(slotIndex, this.Ct);
                    }
                    catch (OperationCanceledException e)
                    {
                        threadErrors.Add(e.Message);
                    }
                }
            });
        }, this.Ct);

        this.AfterSizeBytes = afterSizeBytes;
        return new CompressionResult { Errors = [.. threadErrors] };
    }
}