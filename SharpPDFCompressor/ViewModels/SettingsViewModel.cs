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

namespace SharpPDFCompressor.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
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
            StoreContext storeContext = StoreContext.GetDefault();
            IReadOnlyList<StorePackageUpdate> updates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();
            return updates;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            this.UpdateInProgress = false;
        }
    }

    public async Task RequestDownloadAndInstallUpdatesAsync(IReadOnlyList<StorePackageUpdate> updates)
    {
        StoreContext storeContext = StoreContext.GetDefault();
        await storeContext.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
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