using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace SharpPDFCompressor.ViewModels;

public partial class ProgressDialogViewModel : ObservableObject
{
    [ObservableProperty] public partial double ProgressValue { get; set; }
    [ObservableProperty] public partial string? FileText { get; set; }
    [ObservableProperty] public partial bool ShowError { get; set; }
    [ObservableProperty] public partial List<Exception>? ErrorList { get; set; }
    [ObservableProperty] public partial object? SelectedListError { get; set; }
    [ObservableProperty] public partial bool TipShown { get; set; }

    [RelayCommand]
    public async Task CopyErrorToClipboard()
    {
        var textToCopy = SelectedListError?.ToString();
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
}