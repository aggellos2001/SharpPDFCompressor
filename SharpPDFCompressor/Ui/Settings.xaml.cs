using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SharpPDFCompressor.DataModels;
using SharpPDFCompressor.ViewModels;
using System;
using Windows.System;

namespace SharpPDFCompressor.Ui;

public sealed partial class Settings : Page
{
    private bool _isThemeSelectorInitialized;
    private bool _isUpdatingThemeSelector;
    private bool _isLanguageSelectorInitialized;
    private bool _isUpdatingLanguageSelector;
    public Settings()
    {
        this.InitializeComponent();
        this.Loaded += this.Settings_Loaded;
    }

    public SettingsViewModel ViewModel { get; } = new();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (App.MainWindow is MainWindow mainWindow)
        {
            this.UpdateThemeSelection(mainWindow.CurrentTheme);
            this.UpdateLanguageSelection(App.Settings.Language, mainWindow.CurrentLanguage);
        }

        this._isThemeSelectorInitialized = true;
        this._isLanguageSelectorInitialized = true;
    }

    internal void UpdateThemeSelection(MainWindow.AppTheme theme)
    {
        this._isUpdatingThemeSelector = true;
        this.ThemeComboBox.SelectedIndex = (int)theme;
        this._isUpdatingThemeSelector = false;
    }

    private void ThemeSelection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!this._isThemeSelectorInitialized || this._isUpdatingThemeSelector ||
            this.ThemeComboBox.SelectedIndex is < 0 or > 2)
        {
            return;
        }

        if (App.MainWindow is MainWindow mainWindow)
        {
            MainWindow.AppTheme selectedTheme = (MainWindow.AppTheme)this.ThemeComboBox.SelectedIndex;
            if (mainWindow.CurrentTheme != selectedTheme)
            {
                mainWindow.ApplyTheme(selectedTheme);
            }
        }
    }

    internal void UpdateLanguageSelection(LanguagePreference preference, MainWindow.AppLanguage language)
    {
        this._isUpdatingLanguageSelector = true;
        this.LanguageComboBox.SelectedIndex = (int)preference;
        this._isUpdatingLanguageSelector = false;
    }

    private async void LanguageSelection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!this._isLanguageSelectorInitialized || this._isUpdatingLanguageSelector ||
            this.LanguageComboBox.SelectedIndex is < 0 or > 2)
        {
            return;
        }

        if (App.MainWindow is MainWindow mainWindow)
        {
            if (this.LanguageComboBox.SelectedIndex == 0)
            {
                await mainWindow.ApplySystemLanguage();
            }
            else
            {
                await mainWindow.ApplyLanguage((MainWindow.AppLanguage)(this.LanguageComboBox.SelectedIndex - 1));
            }
        }
    }

    private async void AppDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsStore.EnsureAppDataFolderExists();
        await Launcher.LaunchFolderPathAsync(AppSettingsStore.AppDataFolderPath);
    }

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

    private async void LicenseCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is SettingsCard card && card.Tag is PackageLicense license &&
            !string.IsNullOrWhiteSpace(license.ActionableUrl))
        {
            Uri uri = new(license.ActionableUrl);
            await Launcher.LaunchUriAsync(uri);
        }
    }
}