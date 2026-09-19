using Ghostscript.NET;
using Ghostscript.NET.Processor;
using Microsoft.Windows.ApplicationModel.Resources;
using SharpPDFCompressor.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpPDFCompressor.ViewModels;

public class CompressionResult
{
    public List<string> Errors { get; init; } = [];
    public bool HasErrors => this.Errors.Count > 0;
}

public abstract class Compressor
{
    //private variables set by the compressor only
    private static readonly string DllPath = Path.Combine(AppContext.BaseDirectory, "Runtimes", "gsdll64.dll");
    protected readonly ResourceLoader ResourceLoader = new();
    private BlockingCollection<int>? _availableStatusSlots;

    protected IEnumerable<string> Files { get; set; } = [];
    protected int PdfFilesCount { get; set; }
    protected const string CompressedSuffix = "_compressed";


    // public variables required to be set.
    public bool DeleteOriginalFiles { get; init; }
    public required CancellationToken Ct { get; init; }
    public required IProgress<(double? progressValue, int? workerID, string? message)>? ProgressHandler { get; init; }

    public required string InputFilesPath { get; init; }
    public string CompressionLevel { get; init; } = "ebook";
    public int NumOfThreads { get; init; } = 4;

    private readonly Object _gsInitLock = new();


    public async Task<CompressionResult> ExecuteCompressAsync()
    {
        CompressionResult result = await this.PreCompressAsync();
        if (result.HasErrors || this.Ct.IsCancellationRequested)
        {
            return result;
        }

        result = await this.CompressAsync();
        if (result.HasErrors || this.Ct.IsCancellationRequested)
        {
            return result;
        }

        result = await this.PostCompressionAsync();
        return result;
    }

    protected abstract Task<CompressionResult> PreCompressAsync();
    protected abstract CompressionResult PostFileCompress(string originalFilePath, string compressedFilePath);

    protected abstract Task<CompressionResult> PostCompressionAsync();

    private async Task<CompressionResult> CompressAsync()
    {
        ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = this.NumOfThreads };
        ConcurrentBag<string> threadErrors = [];

        await Task.Run(() =>
        {
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
                        threadErrors.Add(this.ResourceLoader.GetString("GenericError"));
                        return;
                    }

                    string extension = Path.GetExtension(file);
                    if (!extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    string safeFileName = AppUtils.GetSafeFileName(file, CompressedSuffix);
                    string compressedFileName = Path.Combine(directoryName, $"{safeFileName}");
                    int counter = 1;
                    // Keep appending a counter until we find a filename that doesn't exist yet
                    string safeFileNameWithoutExtension = Path.GetFileNameWithoutExtension(safeFileName);
                    while (File.Exists(compressedFileName))
                    {
                        compressedFileName = Path.Combine(directoryName,
                            $"{safeFileNameWithoutExtension} ({counter}){extension}");
                        counter++;
                    }

                    this.ProgressHandler?.Report((
                        null, slotIndex, $"{this.ResourceLoader.GetString("Compressing")} {file}"));

                    GCMemoryInfo memInfo = GC.GetGCMemoryInfo();
                    long freeMemoryBytes = memInfo.TotalAvailableMemoryBytes - memInfo.MemoryLoadBytes;
                    long bufferSpace = Math.Min((long)(freeMemoryBytes * 0.15), 1_000_000_000);
                    bufferSpace = Math.Max(bufferSpace, 50_000_000);
                    long bandBufferSpace = bufferSpace / 2;


                    List<string> arguments =
                    [
                        "-empty",
                        "-dQUIET",
                        "-dSAFER",
                        "-dBATCH",
                        "-dNOPAUSE",
                        "-sDEVICE=pdfwrite",
                        $"-dPDFSETTINGS=/{this.CompressionLevel}",
                        // $"-dBufferSpace={bufferSpace}",
                        // $"-dBandBufferSpace={bandBufferSpace}",
                        $"-sOutputFile={compressedFileName}",
                        "-f",
                        file
                    ];
                    GhostscriptVersionInfo gsVersion = new(
                        new Version(10, 07, 1),
                        DllPath,
                        string.Empty,
                        GhostscriptLicense.GPL
                    );
                    GhostscriptProcessor gsProcessor;

                    lock (this._gsInitLock)
                    {
                        gsProcessor = new GhostscriptProcessor();
                        gsProcessor.Processing += (sender, _) =>
                        {
                            try
                            {
                                if (this.Ct is not { IsCancellationRequested: true })
                                {
                                    return;
                                }

                                if (sender is GhostscriptProcessor processor)
                                {
                                    processor.StopProcessing();
                                }
                            }
                            catch (ObjectDisposedException e)
                            {
                                threadErrors.Add(e.Message);
                            }
                        };
                    }

                    using (gsProcessor)
                    {
                        gsProcessor.Process([.. arguments]);
                    }

                    this.PostFileCompress(file, compressedFileName);
                }
                catch (Exception exception)
                {
                    threadErrors.Add(exception.Message);
                }
                finally
                {
                    this.ProgressHandler?.Report((
                        1.0 / this.PdfFilesCount * 100,
                        slotIndex,
                        "Done..."));
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

        return new CompressionResult { Errors = [.. threadErrors] };
    }
}