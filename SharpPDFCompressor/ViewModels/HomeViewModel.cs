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
using System.Collections.Generic;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompressionButtonEnabled))]
    public partial string FilePath { get; set; } = string.Empty;

    public bool CompressionButtonEnabled => !string.IsNullOrWhiteSpace(FilePath) && Path.Exists(FilePath);

    [ObservableProperty] public partial string? CompressionLevel { get; set; } = "ebook";

    [ObservableProperty] public partial bool DeleteOriginalFiles { get; set; }

    public bool IsTypeSelected(string type)
    {
        return CompressionLevel == type;
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
        using GhostscriptProcessor gsProcessor = new(gsVersion);
        using CancellationTokenSource cts = new();

        gsProcessor.Processing += (_, _) =>
        {
            try
            {
                if (cts is { IsCancellationRequested: true })
                {
                    gsProcessor.StopProcessing();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        };

        XamlUICommand buttonCancelCommand = new();
        buttonCancelCommand.ExecuteRequested += (s, q) =>
        {
            try
            {
                if (cts is { IsCancellationRequested: false })
                {
                    cts.Cancel();
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
        dialog.ShowAsync();

        string inputPath = FilePath;
        string quality = CompressionLevel ?? "ebook";
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
                    await ReaderFactory.OpenAsyncReader(stream, cancellationToken: cts.Token);

                string? parentDir = Path.GetDirectoryName(inputPath);
                if (parentDir == null)
                {
                    errors.Add(new Exception("Something went wrong!"));
                }
                //string zipFileName = Path.GetFileNameWithoutExtension(inputPath);


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
                        cts.Token);
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
            await Task.Run(() =>
            {
                foreach (string file in files)
                {
                    if (cts.Token.IsCancellationRequested)
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
                        continue;
                    }

                    App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                    {
                        progressDialogViewModel.FileText = $"{_resourceLoader.GetString("Compressing")} {file}";
                    });

                    try
                    {
                        string compressedFileName =
                            Path.Combine(directoryName, $"{fileName}_compressed{extension}");

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
                        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                        {
                            progressDialogViewModel.ProgressValue += 1.0 / pdfFilesCount * 100;
                        });
                    }
                }
            }, cts.Token);
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
                        }, cts.Token);

                await writer.WriteAllAsync(inputPath, "*", SearchOption.AllDirectories, cts.Token);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
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


        dialog.PrimaryButtonText = _resourceLoader.GetString("Finish");
        dialog.CloseButtonText = string.Empty;
        dialog.IsPrimaryButtonEnabled = true;


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