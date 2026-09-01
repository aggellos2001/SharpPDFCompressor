using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace SharpPDFCompressor.ViewModels;

public partial class ProgressDialogViewModel : ObservableObject
{
    [ObservableProperty] public partial double ProgressValue { get; set; }
    [ObservableProperty] public partial string? CurrentFileBeingCompressed { get; set; }
    [ObservableProperty] public partial List<string>? ErrorList { get; set; }

    
    [ObservableProperty] public partial bool ShowError { get; set; }
    [ObservableProperty] public partial string? SelectedListError { get; set; }
    [ObservableProperty] public partial bool TipShown { get; set; }

    public ObservableCollection<string> WorkerFileStatuses { get; } = [];


    [RelayCommand]
    public async Task CopyErrorToClipboard()
    {
        string textToCopy = this.SelectedListError ?? "";
        if (!string.IsNullOrEmpty(textToCopy))
        {
            DataPackage dataPackage = new();
            dataPackage.SetText(textToCopy);
            Clipboard.SetContent(dataPackage);

            this.TipShown = true;
            await Task.Delay(2000);
            this.TipShown = false;
        }

        this.SelectedListError = null;
    }

    public void InitializeWorkers(int maxWorkers)
    {
        this.WorkerFileStatuses.Clear();
        for (int i = 0; i < maxWorkers; i++)
        {
            this.WorkerFileStatuses.Add("Waiting...");
        }
    }
}