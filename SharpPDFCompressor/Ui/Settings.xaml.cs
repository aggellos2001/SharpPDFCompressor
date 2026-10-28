using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SharpPDFCompressor.DataModels;
using SharpPDFCompressor.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Services.Store;
using Windows.System;
using WinUI3Localizer;

namespace SharpPDFCompressor.Ui;

public sealed partial class Settings : Page
{
    private bool _isLanguageSelectorInitialized;
    private bool _isThemeSelectorInitialized;
    private bool _isUpdatingLanguageSelector;
    private bool _isUpdatingThemeSelector;

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

        this.UpdateCheckCard.Visibility = IsPackaged() ? Visibility.Visible : Visibility.Collapsed;
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

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        this.CheckForUpdatesButton.IsEnabled = false;
        this.UpdateProgressRing.Visibility = Visibility.Visible;

        try
        {
            IReadOnlyList<StorePackageUpdate>? updates;
            try
            {
                updates = await this.ViewModel.CheckForUpdatesAsync();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Store update check failed: {exception}");
                await this.ShowErrorDialog(
                    "UpdateCheckErrorTitle",
                    "UpdateCheckErrorMessage",
                    true,
                    GetExceptionDetails(exception));
                return;
            }

            if (updates is null)
            {
                await this.ShowErrorDialog(
                    "UpdateCheckErrorTitle",
                    "UpdateCheckErrorMessage",
                    true);
                return;
            }

            if (updates.Count > 0)
            {
                ContentDialog dialog = new()
                {
                    XamlRoot = this.XamlRoot,
                    Title = new TextBlock { Text = this.GetLocalized("UpdateAvailableTitle") },
                    Content =
                        new TextBlock
                        {
                            Text = this.GetLocalized("UpdateAvailableMessage"), TextWrapping = TextWrapping.Wrap
                        },
                    PrimaryButtonText = this.GetLocalized("UpdateNowButton"),
                    CloseButtonText = this.GetLocalized("DismissButton")
                };

                ContentDialogResult dialogResult = await dialog.ShowAsync();

                if (dialogResult == ContentDialogResult.Primary)
                {
                    await this.InstallUpdatesAsync(updates);
                }
            }
            else
            {
                ContentDialog dialog = new()
                {
                    XamlRoot = this.XamlRoot,
                    Title = new TextBlock { Text = this.GetLocalized("UpToDateTitle") },
                    Content = new TextBlock { Text = this.GetLocalized("UpToDateMessage") },
                    CloseButtonText = this.GetLocalized("OkButton")
                };

                await dialog.ShowAsync();
            }
        }
        finally
        {
            this.CheckForUpdatesButton.IsEnabled = true;
            this.UpdateProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private async Task InstallUpdatesAsync(IReadOnlyList<StorePackageUpdate> updates)
    {
        try
        {
            StorePackageUpdateResult result = await this.ViewModel.RequestDownloadAndInstallUpdatesAsync(updates);
            if (result.OverallState == StorePackageUpdateState.Canceled)
            {
                return;
            }

            if (result.OverallState != StorePackageUpdateState.Completed)
            {
                Debug.WriteLine($"Store update installation ended with status {result.OverallState}.");
                await this.ShowErrorDialog(
                    "UpdateInstallErrorTitle",
                    "UpdateInstallStatusMessage",
                    true,
                    messageArgument: result.OverallState.ToString());
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Store update installation failed: {exception}");
            await this.ShowErrorDialog(
                "UpdateInstallErrorTitle",
                "UpdateInstallErrorMessage",
                true,
                GetExceptionDetails(exception));
        }
    }

    private async Task ShowErrorDialog(
        string titleKey,
        string messageKey,
        bool offerStoreNavigation = false,
        string? diagnosticDetails = null,
        string? messageArgument = null)
    {
        string message = messageArgument is null
            ? this.GetLocalized(messageKey)
            : string.Format(this.GetLocalized(messageKey), messageArgument);
        if (diagnosticDetails is not null)
        {
            message =
                $"{message}\n\n{string.Format(this.GetLocalized("UpdateErrorDetailsMessage"), diagnosticDetails)}";
        }

        ContentDialog dialog = new()
        {
            XamlRoot = this.XamlRoot,
            Title = new TextBlock { Text = this.GetLocalized(titleKey) },
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = offerStoreNavigation ? this.GetLocalized("OpenMicrosoftStoreButton") : null,
            PrimaryButtonStyle =
                offerStoreNavigation ? (Style)Application.Current.Resources["AccentButtonStyle"] : null,
            CloseButtonText = this.GetLocalized("OkButton")
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (offerStoreNavigation && result == ContentDialogResult.Primary)
        {
            try
            {
                bool launched = await Launcher.LaunchUriAsync(new Uri("ms-windows-store://downloadsandupdates"));
                if (!launched)
                {
                    Debug.WriteLine("The Microsoft Store Updates and downloads page could not be launched.");
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to launch the Microsoft Store Updates and downloads page: {exception}");
            }
        }
    }

    private static string GetExceptionDetails(Exception exception)
    {
        return $"{exception.GetType().Name}, HRESULT 0x{exception.HResult:X8}";
    }

    private string GetLocalized(string key)
    {
        return Localizer.Get().GetLocalizedString(key);
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Package.Current;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}