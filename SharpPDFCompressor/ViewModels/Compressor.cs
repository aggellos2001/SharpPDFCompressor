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
    public bool HasErrors => Errors.Count > 0;
}

public abstract class Compressor
{
    
    // public variables required to be set.
    public required string FilePath { get; init; }
    public string CompressionLevel { get; init; } = "ebook";
    public int NumOfThreads { get; init; } = 4;

    //private variables set by the compressor only
    private static readonly string DllPath = Path.Combine(AppContext.BaseDirectory, "Runtimes", "gsdll64.dll");
    private const string CompressedSuffix = "_compressed";
    private readonly CancellationToken _ct;
    private readonly ResourceLoader _resourceLoader = new();
    private readonly IProgress<(double? progressValue,int? workerID, string? message)>? _progress;
    private readonly IEnumerable<string> _files;
    private ConcurrentQueue<int> _availableStatusSlots;


    protected Compressor(IProgress<(double? progressValue,int? workerID, string? message)> progressHandler, Action onCancel, CancellationToken ct = default)
    {
        _ct = ct;
        _ct.Register(onCancel);
        this._progress = progressHandler;
        _availableStatusSlots = new(Enumerable.Range(0, NumOfThreads));
    }

    public async Task<CompressionResult> ExecuteCompressAsync()
    {
        CompressionResult result = await this.PreCompressAsync();
        if (result.HasErrors || this._ct.IsCancellationRequested) return result;
        result = await this.CompressAsync();
        if (result.HasErrors || this._ct.IsCancellationRequested) return result;
        result = await this.PostCompressAsync();
        return result;
    }

    protected abstract Task<CompressionResult> PreCompressAsync();

    private async Task<CompressionResult> CompressAsync()
    {
        ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = NumOfThreads };
        ConcurrentBag<string> threadErrors = [];
        
        await Task.Run(() =>
            {
                Parallel.ForEach(_files, parallelOptions, file =>
                {
                    if (this._ct.IsCancellationRequested)
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

                        this._progress?.Report();
                        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                        {
                            progressDialogViewModel.WorkerFileStatuses[slotIndex] =
                                $"{this._resourceLoader.GetString("Compressing")} {file}";
                        });

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
                            $"-dPDFSETTINGS=/{CompressionLevel}",
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
                                if (this._ct is not { IsCancellationRequested: true })
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
                        _availableStatusSlots.Enqueue(slotIndex);

                        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                        {
                            progressDialogViewModel.WorkerFileStatuses[slotIndex] = "Done...";
                            progressDialogViewModel.ProgressValue += 1.0 / pdfFilesCount * 100;
                        });
                    }
                });
            }, this._ct);

        return new CompressionResult(){Errors = [..threadErrors]};
    }

    protected abstract Task<CompressionResult> PostCompressAsync();
}