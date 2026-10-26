using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpPDFCompressor.DataModels;
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
using WinUI3Localizer;
using static System.Boolean;

namespace SharpPDFCompressor.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly ILocalizer _localizer = Localizer.Get();
    private readonly List<string> _selectedPdfPaths = [];
    private CancellationTokenSource? _cts;
    private string _deleteOriginalCardHeaderKey = "DeleteOriginalCardHeader";
    private string _deleteOriginalCardDescriptionKey = "DeleteOriginalCardDescription";

    public HomeViewModel()
    {
        this._localizer.LanguageChanged += this.Localizer_LanguageChanged;
        this.UpdateDeleteOriginalCardLabels();
        this.MaxDecompressedSizeMb = App.Settings.MaxDecompressedSizeMb;
    }

    private void Localizer_LanguageChanged(object? sender, LanguageChangedEventArgs e) =>
        this.UpdateDeleteOriginalCardLabels();

    private void UpdateDeleteOriginalCardLabels()
    {
        this.DeleteOriginalCardHeader = this._localizer.GetLocalizedString(this._deleteOriginalCardHeaderKey);
        this.DeleteOriginalCardDescription = this._localizer.GetLocalizedString(this._deleteOriginalCardDescriptionKey);
    }

    private void SetDeleteOriginalCardLabelKeys(string keyPrefix)
    {
        this._deleteOriginalCardHeaderKey = $"{keyPrefix}CardHeader";
        this._deleteOriginalCardDescriptionKey = $"{keyPrefix}CardDescription";
        this.UpdateDeleteOriginalCardLabels();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompressionButtonEnabled))]
    public partial string FilePath { get; private set; } = string.Empty;

    public bool CompressionButtonEnabled => this._selectedPdfPaths.Count > 0
        ? this._selectedPdfPaths.TrueForAll(File.Exists)
        : !string.IsNullOrWhiteSpace(this.FilePath) && Path.Exists(this.FilePath);

    private void SetFilePath(string? path)
    {
        this._selectedPdfPaths.Clear();
        this.FilePath = path ?? string.Empty;
    }

    private void SetPdfSelection(IEnumerable<string> paths)
    {
        this._selectedPdfPaths.Clear();
        this._selectedPdfPaths.AddRange(paths);
        this.FilePath = string.Join(Environment.NewLine, this._selectedPdfPaths);
    }

    private static bool IsPdf(string path) => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

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

    [ObservableProperty]
    public partial int MaxDecompressedSizeMb { get; set; } = AppSettings.DefaultMaxDecompressedSizeMb;

    partial void OnMaxDecompressedSizeMbChanged(int value)
    {
        App.Settings.MaxDecompressedSizeMb = value;
        AppSettingsStore.Save(App.Settings);
    }

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
            IReadOnlyList<StorageFile> files = await openPicker.PickMultipleFilesAsync();
            if (files.Count == 0)
            {
                this.SetFilePath(string.Empty);
                return;
            }

            if (files.Count > 1)
            {
                List<string> pdfPaths = [];
                foreach (StorageFile selectedFile in files)
                {
                    if (IsPdf(selectedFile.Path))
                    {
                        pdfPaths.Add(selectedFile.Path);
                    }
                }

                this.SetPdfSelection(pdfPaths);
                this.DeleteOriginalFiles = false;
                this.EnabledDeleteOriginalFilesButton = true;
                this.SetDeleteOriginalCardLabelKeys("DeleteOriginal");
                return;
            }

            StorageFile file = files[0];
            this.SetFilePath(file.Path);
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

            this.SetDeleteOriginalCardLabelKeys("DeleteOriginal");
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
            this.SetFilePath(folder?.Path);

            this.DeleteOriginalFiles = false;
            this.EnabledDeleteOriginalFilesButton = true;

            this.SetDeleteOriginalCardLabelKeys("CreateNewFolder");
        }
    }

    public void OnDragOver(object _, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = this._localizer.GetLocalizedString("DragUICaption");
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
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            if (items.Count == 0)
            {
                return;
            }

            StorageFolder? droppedFolder = null;
            foreach (IStorageItem item in items)
            {
                if (item is StorageFolder folder)
                {
                    droppedFolder = folder;
                    break;
                }
            }

            if (droppedFolder is not null)
            {
                this.SetFilePath(droppedFolder.Path);
                this.DeleteOriginalFiles = false;
                this.EnabledDeleteOriginalFilesButton = true;
                this.SetDeleteOriginalCardLabelKeys("CreateNewFolder");
            }
            else if (items.Count == 1 && items[0] is StorageFile archiveFile &&
                     ArchiveFactory.IsArchive(archiveFile.Path, out ArchiveType? archiveType) && archiveType is not null)
            {
                this.SetFilePath(archiveFile.Path);
                this.DeleteOriginalFiles = false;
                this.SetDeleteOriginalCardLabelKeys("DeleteOriginal");
                this.EnabledDeleteOriginalFilesButton = false;
            }
            else
            {
                List<string> pdfPaths = [];
                foreach (IStorageItem item in items)
                {
                    if (item is StorageFile file && IsPdf(file.Path))
                    {
                        pdfPaths.Add(file.Path);
                    }
                }

                this.SetPdfSelection(pdfPaths);
                this.DeleteOriginalFiles = false;
                this.EnabledDeleteOriginalFilesButton = true;
                this.SetDeleteOriginalCardLabelKeys("DeleteOriginal");
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
            this.SetFilePath(string.Empty);
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

        ProgressDialog dialog = new(progressDialogViewModel)
        {
            XamlRoot = xamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            Title = this._localizer.GetLocalizedString("Compressing"),
            PrimaryButtonText = string.Empty,
            CloseButtonText = this._localizer.GetLocalizedString("Cancel"),
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
            dialog.Title = this._localizer.GetLocalizedString("CancelOperation");
            progressDialogViewModel.ProgressSummary =
                this._localizer.GetLocalizedString("CancelOperationExplanation");
            dialog.CloseButtonText = this._localizer.GetLocalizedString("PleaseWait");
        };

        int highestCompletedFileCount = 0;
        int compressionFinished = 0;
        Progress<(double? progressValue, int? workerID, string? message, int? completedFiles, int? totalFiles)>
            progressHandler = new(report =>
            {
                if (Volatile.Read(ref compressionFinished) == 1)
                {
                    return;
                }

                if (report.progressValue is { } progressValue)
                {
                    progressDialogViewModel.ProgressValue += progressValue;
                }

                if (report.message is { } message && message != "Done...")
                {
                    if (report.workerID is { } workerId)
                    {
                        progressDialogViewModel.UpdateWorkerStatus(workerId, message);
                    }
                    else
                    {
                        progressDialogViewModel.ProgressSummary = message;
                    }
                }

                if (report.completedFiles is { } completedFiles && report.totalFiles is { } totalFiles &&
                    totalFiles > 0)
                {
                    highestCompletedFileCount = Math.Max(highestCompletedFileCount, completedFiles);
                    double percent = (double)highestCompletedFileCount / totalFiles * 100;
                    progressDialogViewModel.ProgressSummary = string.Format(
                        CultureInfo.CurrentCulture,
                        this._localizer.GetLocalizedString("CompressionProgressCounter"),
                        highestCompletedFileCount,
                        totalFiles,
                        percent);
                }
            });

        Compressor? compressor = null;

        if (this._selectedPdfPaths.Count > 0)
        {
            string[] selectedPdfPaths = [.. this._selectedPdfPaths];
            compressor = new FileCompressor
            {
                InputFilesPath = selectedPdfPaths[0],
                InputFilePaths = selectedPdfPaths,
                NumOfThreads = int.Parse(this.ParallelismLevel),
                ProgressHandler = progressHandler,
                CompressImages = this.CompressImages,
                CompressedSuffix = this.CompressedSuffix,
                IgnoreCompressedFiles = this.IgnoreCompressedFiles,
                DeleteOriginalFiles = this.DeleteOriginalFiles,
                Ct = this._cts.Token
            };
        }
        else if (!Directory.Exists(this.FilePath) && ArchiveFactory.IsArchive(this.FilePath, out ArchiveType? isArchiveType))
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
                    MaxDecompressedSizeMb = this.MaxDecompressedSizeMb,
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
        Volatile.Write(ref compressionFinished, 1);
        progressDialogViewModel.ProgressSummary = string.Empty;

        if (compressionResult.BeforeSizeBytes is { } beforeSize && compressionResult.AfterSizeBytes is { } afterSize)
        {
            long reduction = beforeSize - afterSize;
            double reductionPercent = beforeSize == 0 ? 0 : (double)reduction / beforeSize * 100;
            progressDialogViewModel.BeforeSize = FormatFileSize(beforeSize);
            progressDialogViewModel.AfterSize = FormatFileSize(afterSize);
            progressDialogViewModel.ReductionSize = FormatFileSize(Math.Abs(reduction));
            progressDialogViewModel.ReductionPercent = Math.Round(reductionPercent, 1);
            progressDialogViewModel.SizeSummaryVisible = true;

            progressDialogViewModel.UpdateWorkerStatus(
                0,
                string.Format(
                    CultureInfo.CurrentCulture,
                    this._localizer.GetLocalizedString("CompressionDoneSummary"),
                    progressDialogViewModel.BeforeSize,
                    progressDialogViewModel.AfterSize,
                    progressDialogViewModel.ReductionSize,
                    progressDialogViewModel.ReductionPercent));
        }
        else
        {
            progressDialogViewModel.UpdateWorkerStatus(0, this._localizer.GetLocalizedString("CompressionDone"));
        }

        dialog.PrimaryButtonText = this._localizer.GetLocalizedString("Finish");
        dialog.CloseButtonText = string.Empty;
        dialog.IsPrimaryButtonEnabled = true;
        progressDialogViewModel.ProgressValue = 100;

        if (!compressionResult.HasErrors)
        {
            dialog.Title = this._localizer.GetLocalizedString("Success");
        }
        else
        {
            progressDialogViewModel.ShowError = true;
            progressDialogViewModel.ErrorList = compressionResult.Errors;
            progressDialogViewModel.ProgressSummary = string.Format(
                CultureInfo.CurrentCulture,
                this._localizer.GetLocalizedString("FailedToCompressPdfFiles"),
                compressionResult.Errors.Count);
            dialog.Title = this._localizer.GetLocalizedString("CompletedWithErrors");
        }

        this.SetFilePath(string.Empty);
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