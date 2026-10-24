using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpPDFCompressor.Ui;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    private readonly ResourceLoader _resourceLoader = new();
    private CancellationTokenSource? _cts;

    public HomeViewModel()
    {
        this.DeleteOriginalCardHeader = this._resourceLoader.GetString("DeleteOriginalFilesSwitch/Header");
        this.DeleteOriginalCardDescription = this._resourceLoader.GetString("DeleteOriginalFilesSwitch/Description");
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompressionButtonEnabled))]
    public partial string FilePath { get; private set; } = string.Empty;

    public bool CompressionButtonEnabled => !string.IsNullOrWhiteSpace(this.FilePath) && Path.Exists(this.FilePath);

    [ObservableProperty] public partial string ParallelismLevel { get; set; } = GetNearestParallelismLevel(Environment.ProcessorCount).ToString();

    private static int GetNearestParallelismLevel(int processorCount)
    {
        int[] levels = [1, 2, 4, 8, 16, 32];
        int minDistance = int.MaxValue;
        int nearest = levels[0];
        foreach (int level in levels)
        {
            int distance = Math.Abs(level - processorCount);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = level;
            }
        }

        return nearest;
    }

    [ObservableProperty] public partial bool CompressImages { get; set; }

    [ObservableProperty] public partial bool DeleteOriginalFiles { get; set; }

    [ObservableProperty] public partial string CompressedSuffix { get; set; } = Compressor.DefaultCompressedSuffix;

    [ObservableProperty] public partial bool IgnoreCompressedFiles { get; set; } = true;

    [ObservableProperty] public partial string DeleteOriginalCardHeader { get; set; }

    [ObservableProperty] public partial string DeleteOriginalCardDescription { get; set; }

    [ObservableProperty] public partial bool EnabledDeleteOriginalFilesButton { get; set; } = true;

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

            if (string.IsNullOrEmpty(this.FilePath))
            {
                return;
            }

            this.DeleteOriginalFiles = false;

            bool isArchive = false;
            if (!Directory.Exists(this.FilePath))
            {
                ArchiveFactory.IsArchive(this.FilePath, out ArchiveType? archiveType);
                if (archiveType is not null)
                {
                    isArchive = true;
                }
            }

            this.EnabledDeleteOriginalFilesButton = !isArchive;

            this.DeleteOriginalCardHeader = this._resourceLoader.GetString("DeleteOriginalFilesSwitch/Header");
            this.DeleteOriginalCardDescription =
                this._resourceLoader.GetString("DeleteOriginalFilesSwitch/Description");
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

            this.DeleteOriginalFiles = false;

            this.DeleteOriginalCardHeader = this._resourceLoader.GetString("CreateNewFolder/Header");
            this.DeleteOriginalCardDescription = this._resourceLoader.GetString("CreateNewFolder/Description");
        }
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

            bool isArchive = false;
            if (!Directory.Exists(droppedItem.Path))
            {
                ArchiveFactory.IsArchive(droppedItem.Path, out ArchiveType? archiveType);
                if (archiveType is not null)
                {
                    isArchive = true;
                }
            }

            switch (droppedItem)
            {
                case StorageFolder folder:
                    this.FilePath = folder.Path;
                    this.DeleteOriginalFiles = false;
                    this.DeleteOriginalCardHeader = this._resourceLoader.GetString("CreateNewFolder/Header");
                    this.DeleteOriginalCardDescription = this._resourceLoader.GetString("CreateNewFolder/Description");
                    break;
                case StorageFile file when file.Path.ToLower().EndsWith("pdf") || isArchive:
                    this.FilePath = file.Path;
                    this.DeleteOriginalFiles = false;
                    this.DeleteOriginalCardHeader = this._resourceLoader.GetString("DeleteOriginalFilesSwitch/Header");
                    this.DeleteOriginalCardDescription =
                        this._resourceLoader.GetString("DeleteOriginalFilesSwitch/Description");
                    this.EnabledDeleteOriginalFilesButton = !isArchive;
                    break;
                default:
                    this.FilePath = "";
                    break;
            }
        }
        finally
        {
            deferral.Complete();
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
        progressDialogViewModel.InitializeWorkers();
        // ConcurrentQueue<int> availableStatusSlots = new(Enumerable.Range(0, maxWorkers));

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
            progressDialogViewModel.CurrentFileBeingCompressed =
                this._resourceLoader.GetString("CancelOperationExplanation");
            dialog.CloseButtonText = this._resourceLoader.GetString("PleaseWait");
        };

        int highestCompletedFileCount = 0;
        Progress<(double? progressValue, int? workerID, string? message, int? completedFiles, int? totalFiles)>
            progressHandler = new(report =>
            {
                if (report.progressValue is { } progressValue)
                {
                    progressDialogViewModel.ProgressValue += progressValue;
                }

                if ((report.workerID, report.message) is ({ } workerId, { } message))
                {
                    progressDialogViewModel.UpdateWorkerStatus(workerId, message);
                }

                if (report.completedFiles is { } completedFiles && report.totalFiles is { } totalFiles &&
                    totalFiles > 0)
                {
                    highestCompletedFileCount = Math.Max(highestCompletedFileCount, completedFiles);
                    double percent = (double)highestCompletedFileCount / totalFiles * 100;
                    progressDialogViewModel.ProgressSummary = string.Format(
                        CultureInfo.CurrentCulture,
                        this._resourceLoader.GetString("CompressionProgressCounter"),
                        highestCompletedFileCount,
                        totalFiles,
                        percent);
                }
            });

        Compressor? compressor = null;

        if (!Directory.Exists(this.FilePath) && ArchiveFactory.IsArchive(this.FilePath, out ArchiveType? isArchiveType))
        {
            if (isArchiveType is not null)
            {
                compressor = new ArchiveCompressor
                {
                    InputFilesPath = this.FilePath,
                    NumOfThreads = int.Parse(this.ParallelismLevel),
                    ProgressHandler = progressHandler,
                    CompressImages = this.CompressImages,
                    CompressedSuffix = this.CompressedSuffix,
                    IgnoreCompressedFiles = this.IgnoreCompressedFiles,
                    DeleteOriginalFiles = this.DeleteOriginalFiles,
                    Ct = this._cts.Token
                };
            }
        }
        else if (Directory.Exists(this.FilePath))
        {
            compressor = new DirectoryCompressor
            {
                InputFilesPath = this.FilePath,
                NumOfThreads = int.Parse(this.ParallelismLevel),
                ProgressHandler = progressHandler,
                CompressImages = this.CompressImages,
                CompressedSuffix = this.CompressedSuffix,
                IgnoreCompressedFiles = this.IgnoreCompressedFiles,
                DeleteOriginalFiles = this.DeleteOriginalFiles,
                Ct = this._cts.Token
            };
        }
        else
        {
            compressor = new FileCompressor
            {
                InputFilesPath = this.FilePath,
                NumOfThreads = int.Parse(this.ParallelismLevel),
                ProgressHandler = progressHandler,
                CompressImages = this.CompressImages,
                CompressedSuffix = this.CompressedSuffix,
                IgnoreCompressedFiles = this.IgnoreCompressedFiles,
                DeleteOriginalFiles = this.DeleteOriginalFiles,
                Ct = this._cts.Token
            };
        }

        if (compressor is null)
        {
            return;
        }


        dialog.ShowAsync();

        CompressionResult compressionResult = await compressor.ExecuteCompressAsync();

        if (compressionResult.BeforeSizeBytes is { } beforeSize && compressionResult.AfterSizeBytes is { } afterSize)
        {
            long reduction = beforeSize - afterSize;
            double reductionPercent = beforeSize == 0 ? 0 : (double)reduction / beforeSize * 100;
            progressDialogViewModel.BeforeSize = FormatFileSize(beforeSize);
            progressDialogViewModel.AfterSize = FormatFileSize(afterSize);
            progressDialogViewModel.ReductionSize = FormatFileSize(Math.Abs(reduction));
            progressDialogViewModel.ReductionPercent = Math.Round(reductionPercent, 1);
            progressDialogViewModel.SizeSummaryVisible = true;
        }

        dialog.PrimaryButtonText = this._resourceLoader.GetString("Finish");
        dialog.CloseButtonText = string.Empty;
        dialog.IsPrimaryButtonEnabled = true;
        progressDialogViewModel.ProgressValue = 100;

        if (!compressionResult.HasErrors)
        {
            progressDialogViewModel.CurrentFileBeingCompressed = this._resourceLoader.GetString("Success");
        }
        else
        {
            progressDialogViewModel.ShowError = true;
            progressDialogViewModel.ErrorList = compressionResult.Errors;
            progressDialogViewModel.CurrentFileBeingCompressed = this._resourceLoader.GetString("Failure");
        }

        this.FilePath = "";
    }

    private static string FormatFileSize(long sizeBytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = sizeBytes;
        int unitIndex = 0;
        while (Math.Abs(size) >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return string.Format(CultureInfo.CurrentCulture, "{0:0.##} {1}", size, units[unitIndex]);
    }
}