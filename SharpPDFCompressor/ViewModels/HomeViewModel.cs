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
    private const string CompressedSuffix = "_compressed";
    private static readonly string DllPath = Path.Combine(AppContext.BaseDirectory, "Runtimes", "gsdll64.dll");
    private readonly ResourceLoader _resourceLoader = new();
    private CancellationTokenSource? _cts;

    public HomeViewModel()
    {
        DeleteOriginalCardHeader = this._resourceLoader.GetString("DeleteOriginalFilesSwitch/Header");
        DeleteOriginalCardDescription = this._resourceLoader.GetString("DeleteOriginalFilesSwitch/Description");
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompressionButtonEnabled))]
    public partial string FilePath { get; private set; } = string.Empty;

    public bool CompressionButtonEnabled => !string.IsNullOrWhiteSpace(this.FilePath) && Path.Exists(this.FilePath);

    [ObservableProperty] public partial string CompressionLevel { get; set; } = "ebook";

    [ObservableProperty] public partial string ParallelismLevel { get; set; } = "4";

    [ObservableProperty] public partial bool DeleteOriginalFiles { get; set; }

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

            bool isArchive = false;
            if (!Directory.Exists(this.FilePath))
            {
                ArchiveFactory.IsArchive(this.FilePath, out ArchiveType? archiveType);
                if (archiveType is not null) isArchive = true;
            }

            this.EnabledDeleteOriginalFilesButton = !isArchive;

            DeleteOriginalCardHeader = this._resourceLoader.GetString("DeleteOriginalFilesSwitch/Header");
            DeleteOriginalCardDescription = this._resourceLoader.GetString("DeleteOriginalFilesSwitch/Description");
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

            DeleteOriginalCardHeader = this._resourceLoader.GetString("CreateNewFolder/Header");
            DeleteOriginalCardDescription = this._resourceLoader.GetString("CreateNewFolder/Description");
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
                if (archiveType is not null) isArchive = true;
            }

            switch (droppedItem)
            {
                case StorageFolder folder:
                    this.FilePath = folder.Path;
                    this.DeleteOriginalCardHeader = this._resourceLoader.GetString("CreateNewFolder/Header");
                    this.DeleteOriginalCardDescription = this._resourceLoader.GetString("CreateNewFolder/Description");
                    break;
                case StorageFile file when file.Path.ToLower().EndsWith("pdf") || isArchive:
                    this.FilePath = file.Path;
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


        // int maxWorkers = int.Parse(this.ParallelismLevel);


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
        progressDialogViewModel.InitializeWorkers(int.Parse(this.ParallelismLevel));
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

        Progress<(double? progressValue, int? workerID, string? message)> progressHandler = new(report =>
        {
            if (report.progressValue is { } progressValue)
            {
                progressDialogViewModel.ProgressValue += progressValue;
            }

            if ((report.workerID, report.message) is ({ } workerId, { } message))
            {
                progressDialogViewModel.WorkerFileStatuses[workerId] = message;
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
                    CompressionLevel = this.CompressionLevel,
                    NumOfThreads = int.Parse(this.ParallelismLevel),
                    ProgressHandler = progressHandler,
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
                CompressionLevel = this.CompressionLevel,
                NumOfThreads = int.Parse(this.ParallelismLevel),
                ProgressHandler = progressHandler,
                DeleteOriginalFiles = this.DeleteOriginalFiles,
                Ct = this._cts.Token
            };
        }
        else
        {
            compressor = new FileCompressor
            {
                InputFilesPath = this.FilePath,
                CompressionLevel = this.CompressionLevel,
                NumOfThreads = int.Parse(this.ParallelismLevel),
                ProgressHandler = progressHandler,
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
}