using CommunityToolkit.Mvvm.ComponentModel;
using SharpPDFCompressor.DataModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Services.Store;
using WinRT.Interop;

namespace SharpPDFCompressor.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private StoreContext? _storeContext;

    public string AppVersion
    {
        get
        {
            try
            {
                PackageVersion version = Package.Current.Id.Version;
                return $"Version {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
            catch (Exception)
            {
                return "Development build";
            }
        }
    }

    [ObservableProperty] public partial ObservableCollection<PackageLicense>? Licenses { get; set; }

    [ObservableProperty] public partial bool UpdateInProgress { get; set; }

    public async Task<IReadOnlyList<StorePackageUpdate>?> CheckForUpdatesAsync()
    {
        try
        {
            _ = Package.Current;
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        this.UpdateInProgress = true;
        try
        {
            StoreContext storeContext = this.GetStoreContext();
            IReadOnlyList<StorePackageUpdate> updates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();
            return updates;
        }
        finally
        {
            this.UpdateInProgress = false;
        }
    }

    public async Task<StorePackageUpdateResult> RequestDownloadAndInstallUpdatesAsync(
        IReadOnlyList<StorePackageUpdate> updates)
    {
        StoreContext storeContext = this.GetStoreContext();
        return await storeContext.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
    }

    private StoreContext GetStoreContext()
    {
        if (this._storeContext is not null)
        {
            return this._storeContext;
        }

        if (App.MainWindow is null)
        {
            throw new InvalidOperationException(
                "The main window is not available to associate with the Store context.");
        }

        StoreContext storeContext = StoreContext.GetDefault();
        IntPtr hWnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(storeContext, hWnd);
        this._storeContext = storeContext;
        return storeContext;
    }

    public async Task LoadLicenses()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, "Assets", "licenses.json");
        if (File.Exists(filePath))
        {
            string jsonText = await File.ReadAllTextAsync(filePath);
            List<PackageLicense>? licenses = JsonSerializer.Deserialize(
                jsonText,
                SettingsJsonContext.Default.ListPackageLicense);
            this.Licenses = new ObservableCollection<PackageLicense>(licenses ?? []);
        }
    }
}