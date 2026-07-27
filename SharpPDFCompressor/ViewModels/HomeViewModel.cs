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
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpPDFCompressor.Ui;
using SharpPDFCompressor.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private static readonly string DllPath = Path.Combine(AppContext.BaseDirectory, "Runtimes", "gsdll64.dll");
    private readonly ResourceLoader _resourceLoader = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompressionButtonEnabled))]
    public partial string FilePath { get; set; } = string.Empty;

    public bool CompressionButtonEnabled => !string.IsNullOrWhiteSpace(FilePath) && Path.Exists(FilePath);

    [ObservableProperty] public partial string CompressionLevel { get; set; } = "ebook";

    [ObservableProperty] public partial string ParallelismLevel { get; set; } = "4";

    [ObservableProperty] public partial bool DeleteOriginalFiles { get; set; }

    public bool IsTypeSelected(string type)
    {
        return CompressionLevel == type;
    }

    public ObservableCollection<string> WorkerFileStatuses { get; } = [];
    public void InitializeWorkers(int maxWorkers)
    {
        WorkerFileStatuses.Clear();
        for (int i = 0; i < maxWorkers; i++)
        {
            WorkerFileStatuses.Add("Waiting...");        }
    }


    [RelayCommand]
    public async Task SelectFile(string? folderPicker)
    {
        bool tryParse = TryParse(folderPicker, out bool folderPickerBool);

        if (!folderPickerBool)
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

            FilePath = file?.Path ?? string.Empty;
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
            FilePath = folder?.Path ?? string.Empty;
        }
    }

    [RelayCommand]
    public async Task Compress(XamlRoot xamlRoot)
    {
        GhostscriptVersionInfo gsVersion = new(
            new Version(10, 07, 1),
            DllPath,
            string.Empty,
            GhostscriptLicense.GPL
        );
        _cts = new CancellationTokenSource();

        this._cts.Token.Register(() =>
        {
            FilePath = "";
        });

        int maxWorkers = int.Parse(ParallelismLevel);
        
        InitializeWorkers(maxWorkers);
        ConcurrentQueue<int> availableStatusSlots = new(Enumerable.Range(0, maxWorkers));


        XamlUICommand buttonCancelCommand = new();
        buttonCancelCommand.ExecuteRequested += (s, q) =>
        {
            try
            {
                if (_cts is { IsCancellationRequested: false })
                {
                    _cts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        };


        ProgressDialogViewModel progressDialogViewModel = new();
        ProgressDialog dialog = new(progressDialogViewModel)
        {
            XamlRoot = xamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            Title = _resourceLoader.GetString("Compressing"),
            PrimaryButtonText = string.Empty,
            CloseButtonText = _resourceLoader.GetString("Cancel"),
            CloseButtonCommand = buttonCancelCommand,
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };
        dialog.CloseButtonClick += (sender, args) =>
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

        string inputPath = FilePath;
        string quality = CompressionLevel;
        List<Exception> errors = [];
        IEnumerable<string> files = [];

        string originalInputPath = string.Empty;
        bool isArchive = false;

        if (!Directory.Exists(inputPath) && File.Exists(inputPath) &&
            ArchiveFactory.IsArchive(inputPath, out ArchiveType? isArchiveType))
        {
            if (isArchiveType != null)
            {
                await using Stream stream = File.OpenRead(inputPath);
                await using IAsyncReader reader =
                    await ReaderFactory.OpenAsyncReader(stream, cancellationToken: _cts.Token);

                string? parentDir = Path.GetDirectoryName(inputPath);
                if (parentDir == null)
                {
                    errors.Add(new Exception("Something went wrong!"));
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
                    await reader.WriteAllToDirectoryAsync(zipExtractionDir,
                        new ExtractionOptions { ExtractFullPath = true, PreserveFileTime = true, Overwrite = true },
                        _cts.Token);
                }
                catch (Exception e)
                {
                    errors.Add(new Exception("Error occured " + e.StackTrace));
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
            errors.Add(new Exception(this._resourceLoader.GetString("InvalidFileException")));
        }

        if (errors.Count == 0)
        {
            ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = maxWorkers };

            await Task.Run(() =>
            {
                Parallel.ForEach(files, parallelOptions, file =>
                {
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

                    if (_cts.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    string? directoryName = Path.GetDirectoryName(file);
                    if (directoryName == null)
                    {
                        errors.Add(new Exception(this._resourceLoader.GetString("GenericError")));
                        return;
                    }

                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string extension = Path.GetExtension(file);

                    if (!file.ToLower().EndsWith("pdf"))
                    {
                        return;
                    }

                    availableStatusSlots.TryDequeue(out int slotIndex);

                    try
                    {
                        string baseNewName = $"{fileName}_compressed";
                        string compressedFileName = Path.Combine(directoryName, $"{baseNewName}{extension}");
                        int counter = 1;

                        // Keep appending a counter until we find a filename that doesn't exist yet
                        while (File.Exists(compressedFileName))
                        {
                            compressedFileName = Path.Combine(directoryName, $"{baseNewName} ({counter}){extension}");
                            counter++;
                        }
                        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                        {
                            progressDialogViewModel.FileText = $"{_resourceLoader.GetString("Compressing")} {file}";
                            WorkerFileStatuses[slotIndex] = $"{_resourceLoader.GetString("Compressing")} {file}";
                        });

                        List<string> arguments =
                        [
                            "-empty",
                            "-dQUIET",
                            "-dSAFER",
                            "-dBATCH",
                            "-dNOPAUSE",
                            "-sDEVICE=pdfwrite",
                            $"-dPDFSETTINGS=/{quality}",
                            $"-sOutputFile={compressedFileName}",
                            "-f",
                            file
                        ];

                        gsProcessor.Process([.. arguments]);

                        if (isArchive || DeleteOriginalFiles)
                        {
                            //if an archive or a folder with remove original files is given
                            //then we remove the original file and rename the old from the
                            //resulting archive or folder
                            File.Delete(file);
                            File.Move(compressedFileName, file);
                        }
                    }
                    catch (Exception exception)
                    {
                        errors.Add(exception);
                    }
                    finally
                    {
                        availableStatusSlots.Enqueue(slotIndex);

                        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                        {
                            progressDialogViewModel.ProgressValue += 1.0 / pdfFilesCount * 100;
                        });
                    }
                });


                App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    progressDialogViewModel.ProgressValue = 100;
                });

            }, _cts.Token);
        }

        if (isArchive)
        {
            try
            {
                string? resultDir = Path.GetDirectoryName(originalInputPath);
                if (resultDir == null)
                {
                    errors.Add(new Exception(this._resourceLoader.GetString("GenericError")));
                    return;
                }

                string originalArchiveName = Path.GetFileNameWithoutExtension(originalInputPath);
                string destName = Path.Combine(resultDir, $"{originalArchiveName}_compressed.zip");

                /*write a new archive with the files from the temp folder to the original
                location where the archive existed
                 */
                await using FileStream stream = File.Create(destName);
                await using IAsyncWriter writer = await WriterFactory
                    .OpenAsyncWriter(stream, ArchiveType.Zip,
                        new WriterOptions(CompressionType.Deflate)
                        {
                            ArchiveEncoding = new ArchiveEncoding { Forced = Encoding.UTF8 }
                        }, _cts.Token);

                await writer.WriteAllAsync(inputPath, "*", SearchOption.AllDirectories, _cts.Token);
            }
            catch (Exception e)
            {
                errors.Add(e);
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

        if (this._cts is { IsCancellationRequested: true } && errors.Count == 0)
        {
            dialog.Hide();
        }
        else
        {
            dialog.PrimaryButtonText = _resourceLoader.GetString("Finish");
            dialog.CloseButtonText = string.Empty;
            dialog.IsPrimaryButtonEnabled = true;
        }

        if (errors.Count == 0)
        {
            progressDialogViewModel.FileText = _resourceLoader.GetString("Success");
        }
        else
        {
            progressDialogViewModel.FileText = _resourceLoader.GetString("Failure");
            progressDialogViewModel.ProgressValue = 100;
            progressDialogViewModel.ShowError = true;
            progressDialogViewModel.ErrorList = errors;
        }

        FilePath = "";
    }

    public void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Drop your file here";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    public async Task OnDrop(object sender, DragEventArgs e)
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