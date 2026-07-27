using CommunityToolkit.Mvvm.ComponentModel;
using SharpPDFCompressor.DataModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel;

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
                return "Unpackaged build";
            }
        }
    }

    [ObservableProperty]
    public partial ObservableCollection<PackageLicense>? Licenses { get; set; }

    public async Task LoadLicenses()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, "Assets", "licenses.json");
        if (File.Exists(filePath))
        {
            string jsonText = await File.ReadAllTextAsync(filePath);
            List<PackageLicense>? licenses = JsonSerializer.Deserialize<List<PackageLicense>>(jsonText);
            Licenses = new ObservableCollection<PackageLicense>(licenses ?? []);
        }
    }
}