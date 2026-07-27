using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace SharpPDFCompressor.ViewModels;

public partial class ProgressDialogViewModel : ObservableObject
{
    [ObservableProperty] public partial double ProgressValue { get; set; }
    [ObservableProperty] public partial string? FileText { get; set; }
    [ObservableProperty] public partial bool ShowError { get; set; }
    [ObservableProperty] public partial List<String>? ErrorList { get; set; }
    [ObservableProperty] public partial string? SelectedListError { get; set; }
    [ObservableProperty] public partial bool TipShown { get; set; }

    public ObservableCollection<string> WorkerFileStatuses { get; } = [];


    [RelayCommand]
    public async Task CopyErrorToClipboard()
    {
        string textToCopy = SelectedListError ?? "";
        if (!string.IsNullOrEmpty(textToCopy))
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(textToCopy);
            Clipboard.SetContent(dataPackage);

            TipShown = true;
            await Task.Delay(2000);
            TipShown = false;
        }

        SelectedListError = null;
    }

    public void InitializeWorkers(int maxWorkers)
    {
        WorkerFileStatuses.Clear();
        for (int i = 0; i < maxWorkers; i++)
        {
            WorkerFileStatuses.Add("Waiting...");
        }
    }
}