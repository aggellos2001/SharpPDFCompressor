using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SharpPDFCompressor.ViewModels;
using System;
using Windows.System;

namespace SharpPDFCompressor.Ui;

public sealed partial class Settings : Page
{
    public Settings()
    {
        this.InitializeComponent();
        this.Loaded += this.Settings_Loaded;
    }

    public SettingsViewModel ViewModel { get; } = new();

    private async void Settings_Loaded(object sender, RoutedEventArgs e)
    {
        this.Loaded -= this.Settings_Loaded;
        await this.ViewModel.LoadLicenses();
    }

    private async void GitHubButtonOpenLink(object sender, RoutedEventArgs e)
    {
        Uri uri = new("https://github.com/aggellos2001/SharpPDFCompressor");
        await Launcher.LaunchUriAsync(uri);
    }

    private async void LogoLinkedInOpenLink(object sender, RoutedEventArgs e)
    {
        Uri uri = new("https://www.linkedin.com/in/apostolos-paschalis-96a8b8420");
        await Launcher.LaunchUriAsync(uri);
    }
}