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
    [ObservableProperty] public partial string? ProgressSummary { get; set; }
    [ObservableProperty] public partial string? BeforeSize { get; set; }
    [ObservableProperty] public partial string? AfterSize { get; set; }
    [ObservableProperty] public partial string? ReductionSize { get; set; }
    [ObservableProperty] public partial double ReductionPercent { get; set; }
    public string? ReductionPercentText => this.ReductionPercent != 0 ? $"({this.ReductionPercent:F1}%)" : null;

    partial void OnReductionPercentChanged(double value)
    {
        this.OnPropertyChanged(nameof(this.ReductionPercentText));
    }

    [ObservableProperty] public partial bool SizeSummaryVisible { get; set; }
    [ObservableProperty] public partial List<string>? ErrorList { get; set; }


    [ObservableProperty] public partial bool ShowError { get; set; }
    [ObservableProperty] public partial string? SelectedListError { get; set; }
    [ObservableProperty] public partial bool TipShown { get; set; }

    public ObservableCollection<string> WorkerFileStatuses { get; } = new();

    public bool HasWorkerStatuses => this.WorkerFileStatuses.Count > 0;

    public Microsoft.UI.Xaml.Visibility WorkerFileStatusesVisibility =>
        this.ShowError ? Microsoft.UI.Xaml.Visibility.Collapsed :
        this.HasWorkerStatuses ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    partial void OnShowErrorChanged(bool value)
    {
        this.OnPropertyChanged(nameof(this.WorkerFileStatusesVisibility));
    }

    private void OnWorkerStatusesChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        this.OnPropertyChanged(nameof(this.HasWorkerStatuses));
        this.OnPropertyChanged(nameof(this.WorkerFileStatusesVisibility));
    }


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

    [RelayCommand]
    public async Task CopyAllErrorsToClipboard()
    {
        if (this.ErrorList is not { Count: > 0 } errors)
        {
            return;
        }

        DataPackage dataPackage = new();
        dataPackage.SetText(string.Join(System.Environment.NewLine, errors));
        Clipboard.SetContent(dataPackage);

        this.TipShown = true;
        await Task.Delay(2000);
        this.TipShown = false;
    }

    public void InitializeWorkers()
    {
        this.WorkerFileStatuses.CollectionChanged -= this.OnWorkerStatusesChanged;
        this.WorkerFileStatuses.CollectionChanged += this.OnWorkerStatusesChanged;
        this.WorkerFileStatuses.Clear();
    }

    public void UpdateWorkerStatus(int workerId, string message)
    {
        this.WorkerFileStatuses.Add(message);
    }
}