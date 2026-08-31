using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ghostscript.NET;
using Ghostscript.NET.Processor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpPDFCompressor.Ui;
using SharpPDFCompressor.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using static System.Boolean;

namespace SharpPDFCompressor.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private const string CompressedSuffix = "_compressed";
    private static readonly string DllPath = Path.Combine(AppContext.BaseDirectory, "Runtimes", "gsdll64.dll");
    private readonly ResourceLoader _resourceLoader = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompressionButtonEnabled))]
    public partial string FilePath { get; private set; } = string.Empty;

    public bool CompressionButtonEnabled => !string.IsNullOrWhiteSpace(this.FilePath) && Path.Exists(this.FilePath);

    [ObservableProperty] public partial string CompressionLevel { get; set; } = "ebook";

    [ObservableProperty] public partial string ParallelismLevel { get; set; } = "4";

    [ObservableProperty] public partial bool DeleteOriginalFiles { get; set; }

    [RelayCommand]
    private async Task SelectFile(string? folderPicker)
    {
        bool tryParse = TryParse(folderPicker, out bool folderPickerBool);

        if (tryParse && !folderPickerBool)
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(App.MainWindow);

            FileOpenPicker openPicker = new()
            {
                FileTypeFilter =
                {
                    ".pdf",
                    ".zip",
                    ".7z",
                    ".rar",
                    ".tar",
                    ".gz",
                    ".tgz",
                    ".bz2"
                },
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.Thumbnail
            };

            InitializeWithWindow.Initialize(openPicker, hWnd);
            StorageFile? file = await openPicker.PickSingleFileAsync();

            this.FilePath = file?.Path ?? string.Empty;
        }
        else
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(App.MainWindow);

            FolderPicker openPicker = new();
            InitializeWithWindow.Initialize(openPicker, hWnd);
            openPicker.ViewMode = PickerViewMode.Thumbnail;
            openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            openPicker.FileTypeFilter.Add(".pdf");
            StorageFolder? folder = await openPicker.PickSingleFolderAsync();
            this.FilePath = folder?.Path ?? string.Empty;
        }
    }

    [RelayCommand]
    private async Task Compress(XamlRoot xamlRoot)
    {
        this._cts = new CancellationTokenSource();

        this._cts.Token.Register(() =>
        {
            this.FilePath = "";
        });

        int maxWorkers = int.Parse(this.ParallelismLevel);


        XamlUICommand buttonCancelCommand = new();
        buttonCancelCommand.ExecuteRequested += (_, _) =>
        {
            try
            {
                if (this._cts is { IsCancellationRequested: false })
                {
                    this._cts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        };

        ProgressDialogViewModel progressDialogViewModel = new();
        progressDialogViewModel.InitializeWorkers(maxWorkers);
        ConcurrentQueue<int> availableStatusSlots = new(Enumerable.Range(0, maxWorkers));

        ProgressDialog dialog = new(progressDialogViewModel)
        {
            XamlRoot = xamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            Title = this._resourceLoader.GetString("Compressing"),
            PrimaryButtonText = string.Empty,
            CloseButtonText = this._resourceLoader.GetString("Cancel"),
            CloseButtonCommand = buttonCancelCommand,
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };
        dialog.CloseButtonClick += (_, args) =>
        {
            args.Cancel = true;
            if (this._cts is { IsCancellationRequested: true })
            {
                return;
            }

            this._cts.Cancel();
            dialog.Title = this._resourceLoader.GetString("CancelOperation");
            progressDialogViewModel.FileText = this._resourceLoader.GetString("CancelOperationExplanation");
            dialog.CloseButtonText = this._resourceLoader.GetString("PleaseWait");
        };

        dialog.ShowAsync();

        string inputPath = this.FilePath;
        string quality = this.CompressionLevel;
        ConcurrentBag<string> threadErrors = [];
        List<string> errors = [];
        IEnumerable<string> files = [];

        string originalInputPath = string.Empty;
        bool isArchive = false;

        if (!Directory.Exists(inputPath) && File.Exists(inputPath) &&
            ArchiveFactory.IsArchive(inputPath, out ArchiveType? isArchiveType))
        {
            if (isArchiveType != null)
            {
                string? parentDir = Path.GetDirectoryName(inputPath);
                if (parentDir == null)
                {
                    errors.Add(this._resourceLoader.GetString("GenericError"));
                }

                // first we extract the archive in the temp folder
                AppUtils.GetTempDir(out string tempDir);
                string zipExtractionDir = Path.Combine(tempDir, Path.GetRandomFileName());
                if (!Directory.Exists(zipExtractionDir))
                {
                    Directory.CreateDirectory(zipExtractionDir);
                }

                try
                {
                    await Task.Run(() =>
                    {
                        using IArchive archive = ArchiveFactory.OpenArchive(inputPath);
                        ExtractionOptions options = new()
                        {
                            ExtractFullPath = true, PreserveFileTime = true, Overwrite = true
                        };
                        foreach (IArchiveEntry entry in archive.Entries.Where(e => !e.IsDirectory))
                        {
                            this._cts.Token.ThrowIfCancellationRequested();
                            entry.WriteToDirectory(zipExtractionDir, options);
                        }
                    }, this._cts.Token);
                }
                catch (OperationCanceledException)
                {
                    dialog.Hide();
                    return;
                }
                catch (Exception e)
                {
                    errors.Add("Error occured " + e.StackTrace);
                }

                // keep the original input path here for cleanup
                isArchive = true;
                originalInputPath = inputPath;
                inputPath = zipExtractionDir;
            }
        }

        int pdfFilesCount = 0;
        if (Directory.Exists(inputPath))
        {
            // Loop through all PDFs in the folder
            files = Directory.EnumerateFiles(inputPath, "*.*", SearchOption.AllDirectories).ToArray();
            pdfFilesCount = files.Count(file => file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        }
        else if (File.Exists(inputPath) && Path.GetExtension(inputPath).ToLower().EndsWith("pdf"))
        {
            files = [inputPath];
            pdfFilesCount = 1;
        }
        else
        {
            errors.Add(this._resourceLoader.GetString("InvalidFileException"));
        }

        progressDialogViewModel.FileText = $"{this._resourceLoader.GetString("CompressingStatus")}";


        if (errors.Count == 0)
        {
            ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = maxWorkers };

            await Task.Run(() =>
            {
                Parallel.ForEach(files, parallelOptions, file =>
                {
                    if (this._cts.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (this._cts.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    availableStatusSlots.TryDequeue(out int slotIndex);

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
                            $"-dPDFSETTINGS=/{quality}",
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
                                if (this._cts is not { IsCancellationRequested: true })
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

                        if (!isArchive && !this.DeleteOriginalFiles)
                        {
                            return;
                        }

                        //if an archive or a folder with remove original files is given
                        //then we remove the original file and rename the old from the
                        //resulting archive or folder
                        File.Delete(file);
                        File.Move(compressedFileName, file);
                    }
                    catch (Exception exception)
                    {
                        threadErrors.Add(exception.Message);
                    }
                    finally
                    {
                        availableStatusSlots.Enqueue(slotIndex);

                        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                        {
                            progressDialogViewModel.WorkerFileStatuses[slotIndex] = "Done...";
                            progressDialogViewModel.ProgressValue += 1.0 / pdfFilesCount * 100;
                        });
                    }
                });
            }, this._cts.Token);
        }

        // If cancellation is request return immediately the method and close the dialog.
        // This code is reached after the try-catch is completed
        if (this._cts is { IsCancellationRequested: true } && errors.Count == 0)
        {
            dialog.Hide();
            this.FilePath = "";
            return;
        }

        progressDialogViewModel.ProgressValue = 100;
        errors.AddRange(threadErrors);

        if (isArchive && errors.Count == 0)
        {
            try
            {
                string? resultDir = Path.GetDirectoryName(originalInputPath);
                if (resultDir == null)
                {
                    errors.Add(this._resourceLoader.GetString("GenericError"));
                    return;
                }

                string originalArchiveName = Path.GetFileNameWithoutExtension(originalInputPath);
                string destName = Path.Combine(resultDir, $"{originalArchiveName}${CompressedSuffix}.zip");
                int counter = 1;
                while (File.Exists(destName))
                {
                    destName = Path.Combine(resultDir, $"{originalArchiveName}${CompressedSuffix}({counter}).zip");
                    counter++;
                }

                /*write a new archive with the files from the temp folder to the original
                location where the archive existed
                 */
                await using FileStream stream = File.Create(destName);
                await using IAsyncWriter writer = await WriterFactory
                    .OpenAsyncWriter(stream, ArchiveType.Zip,
                        new WriterOptions(CompressionType.Deflate)
                        {
                            ArchiveEncoding = new ArchiveEncoding { Forced = Encoding.UTF8 }
                        }, this._cts.Token);

                await writer.WriteAllAsync(inputPath, "*", SearchOption.AllDirectories, this._cts.Token);
            }
            catch (Exception e)
            {
                errors.Add(e.Message);
            }
            finally
            {
                //finally cleanup the temp directory
                if (Directory.Exists(inputPath))
                {
                    Directory.Delete(inputPath, true);
                }
            }
        }


        dialog.PrimaryButtonText = this._resourceLoader.GetString("Finish");
        dialog.CloseButtonText = string.Empty;
        dialog.IsPrimaryButtonEnabled = true;
        progressDialogViewModel.ProgressValue = 100;

        if (errors.Count == 0)
        {
            progressDialogViewModel.FileText = this._resourceLoader.GetString("Success");
        }
        else
        {
            progressDialogViewModel.ShowError = true;
            progressDialogViewModel.ErrorList = errors;
            progressDialogViewModel.FileText = this._resourceLoader.GetString("Failure");
        }

        this.FilePath = "";
    }

    public void OnDragOver(object _, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Drop your file here";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    public async Task OnDrop(object _, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        DragOperationDeferral? deferral = e.GetDeferral();
        try
        {
            IReadOnlyList<IStorageItem>? items = await e.DataView.GetStorageItemsAsync();
            if (items.Count == 0)
            {
                return;
            }

            IStorageItem droppedItem = items[0];

            this.FilePath = droppedItem switch
            {
                StorageFile file => file.Path,
                StorageFolder folder => folder.Path,
                _ => this.FilePath
            };
        }
        finally
        {
            deferral.Complete();
        }
    }
}