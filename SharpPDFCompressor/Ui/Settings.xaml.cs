using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SharpPDFCompressor.DataModels;
using SharpPDFCompressor.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace SharpPDFCompressor.Ui;

public sealed partial class Settings : Page
{

    public SettingsViewModel ViewModel { get; } = new();
    public Settings()
    {
        InitializeComponent();
        this.Loaded += this.Settings_Loaded;
    }

    private async void Settings_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        this.Loaded -= this.Settings_Loaded;
        await ViewModel.LoadLicenses();
    }

    private async void GitHubButtonOpenLink(object sender, RoutedEventArgs e)
    {
        Uri uri = new ("https://github.com/aggellos2001/SharpPDFCompressor");
        await Windows.System.Launcher.LaunchUriAsync(uri);
    }

    private async void LogoLinkedInOpenLink(object sender, RoutedEventArgs e)
    {
        Uri uri = new("https://www.linkedin.com/in/apostolos-paschalis-96a8b8420");
        await Windows.System.Launcher.LaunchUriAsync(uri);
    }
}