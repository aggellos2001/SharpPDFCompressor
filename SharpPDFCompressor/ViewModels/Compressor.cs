using Ghostscript.NET;
using Ghostscript.NET.Processor;
using Microsoft.Windows.ApplicationModel.Resources;
using SharpPDFCompressor.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private const string CompressedSuffix = "_compressed";

    //private variables set by the compressor only
    private static readonly string DllPath = Path.Combine(AppContext.BaseDirectory, "Runtimes", "gsdll64.dll");
    private readonly ConcurrentQueue<int> _availableStatusSlots;
    private readonly ResourceLoader _resourceLoader = new();


    protected Compressor()
    {
        this._availableStatusSlots = new ConcurrentQueue<int>(Enumerable.Range(0, this.NumOfThreads));
    }

    protected abstract IEnumerable<string> Files { get; init; }


    // public variables required to be set.
    public required CancellationToken Ct { get; init; }
    public required IProgress<(double? progressValue, int? workerID, string? message)>? ProgressHandler { get; init; }

    public required string FilePath { get; init; }
    public string CompressionLevel { get; init; } = "ebook";
    public int NumOfThreads { get; init; } = 4;

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

        result = await this.PostCompressAsync();
        return result;
    }

    protected abstract Task<CompressionResult> PreCompressAsync();

    private async Task<CompressionResult> CompressAsync()
    {
        ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = this.NumOfThreads };
        ConcurrentBag<string> threadErrors = [];

        await Task.Run(() =>
        {
            Parallel.ForEach(this.Files, parallelOptions, file =>
            {
                if (this.Ct.IsCancellationRequested)
                {
                    return;
                }

                this._availableStatusSlots.TryDequeue(out int slotIndex);
                try
                {
                    string? directoryName = Path.GetDirectoryName(file);
                    if (directoryName == null)
                    {
                        threadErrors.Add(this._resourceLoader.GetString("GenericError"));
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
                        null, slotIndex, $"{this._resourceLoader.GetString("Compressing")} {file}"));

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
                        $"-dBufferSpace={bufferSpace}",
                        $"-dBandBufferSpace={bandBufferSpace}",
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
                    using GhostscriptProcessor gsProcessor = new(gsVersion);
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
                        catch (ObjectDisposedException)
                        {
                        }
                    };
                    gsProcessor.Process([.. arguments]);
                }
                catch (Exception exception)
                {
                    threadErrors.Add(exception.Message);
                }
                finally
                {
                    this._availableStatusSlots.Enqueue(slotIndex);

                    this.ProgressHandler?.Report((
                        1.0 / this.Files.Count() * 100,
                        slotIndex,
                        "Done..."));
                }
            });
        }, this.Ct);

        return new CompressionResult { Errors = [.. threadErrors] };
    }

    protected abstract Task<CompressionResult> PostCompressAsync();
}