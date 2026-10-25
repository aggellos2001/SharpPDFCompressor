using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SharpPDFCompressor.DataModels;
using SharpPDFCompressor.Ui;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using WinUI3Localizer;
using WinUIEx;

namespace SharpPDFCompressor;

public sealed partial class MainWindow : WindowEx
{
    private static bool IsDebug =>
#if DEBUG
        true;
#else
        false;
#endif

    public enum AppTheme
    {
        Default,
        Light,
        Dark
    }

    public enum AppLanguage
    {
        English,
        Greek
    }

    private AppTheme _currentTheme;
    private object? _selectionBeforeThemeToggle;
    private AppLanguage _currentLanguage;
    private LanguagePreference _languagePreference;

    public AppTheme CurrentTheme => this._currentTheme;
    public AppLanguage CurrentLanguage => this._currentLanguage;

    public string LanguageTooltip { get; private set; } = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        this.InitializeComponent();
        this._currentTheme = (AppTheme)App.Settings.Theme;
        this._languagePreference = App.Settings.Language;
        this._currentLanguage = (AppLanguage)((int)App.Settings.ResolvedLanguage - 1);
        this.UpdateLanguageCode();
        this.ApplyTheme(this._currentTheme);
        //navigates to the home screen when the application opens
        this.HomePageSideButton.IsSelected = true;

        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(this.AppTitleBar);

        if (!MicaController.IsSupported())
        {
            return;
        }

        MicaBackdrop mica = new() { Kind = MicaKind.Base };
        this.SystemBackdrop = mica;

        if (this.Content is FrameworkElement rootElement)
        {
            rootElement.LayoutUpdated += this.OnContentLayoutUpdated;
        }

        if (IsDebug)
        {
            this.AppTitleBar.Subtitle = "Debug build";
        }
    }

    private void SideMenu_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Name is "ThemeToggleSideButton" or "LanguageToggleSideButton")
        {
            this.MainNavigationView.SelectedItem = this._selectionBeforeThemeToggle;
            return;
        }

        string? tag = args.SelectedItemContainer?.Name;

        if (args.IsSettingsSelected)
        {
            this.ContentFrame.Navigate(typeof(Settings));
        }

        switch (tag)
        {
            case "HomePageSideButton":
                this.ContentFrame.Navigate(typeof(Home));
                break;
        }
    }

    private void OnContentLayoutUpdated(object? sender, object e)
    {
        if (this.Content is not FrameworkElement rootElement)
        {
            return;
        }

        rootElement.LayoutUpdated -= this.OnContentLayoutUpdated;

        // Use the DesiredSize of the root element
        //double targetWidth = rootElement.DesiredSize.Width;
        double targetHeight = rootElement.DesiredSize.Height;

        this.SetWindowSize(1250, targetHeight + 10.0);
        this.CenterOnScreen();

        this.Activate();
    }

    private void ThemeToggle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Prevent the NavigationView from navigating
        e.Handled = true;
        this._selectionBeforeThemeToggle = this.MainNavigationView.SelectedItem;

        // Cycle through themes: Default -> Light -> Dark -> Default
        AppTheme nextTheme = this._currentTheme switch
        {
            AppTheme.Default => AppTheme.Light,
            AppTheme.Light => AppTheme.Dark,
            _ => AppTheme.Default
        };

        this.ApplyTheme(nextTheme);
    }

    private async void LanguageToggle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        this._selectionBeforeThemeToggle = this.MainNavigationView.SelectedItem;

        // Cycle through preferences: System -> English -> Greek -> System
        LanguagePreference nextPreference = this._languagePreference switch
        {
            LanguagePreference.System => LanguagePreference.English,
            LanguagePreference.English => LanguagePreference.Greek,
            _ => LanguagePreference.System
        };

        if (nextPreference == LanguagePreference.System)
        {
            await this.ApplySystemLanguage();
        }
        else
        {
            AppLanguage language = nextPreference == LanguagePreference.English
                ? AppLanguage.English
                : AppLanguage.Greek;
            await this.ApplyLanguage(language);
        }
    }

    public async Task ApplyLanguage(AppLanguage language)
    {
        LanguagePreference preference = (LanguagePreference)((int)language + 1);
        App.Settings.Language = preference;
        this._languagePreference = preference;
        await this.UpdateLanguage(language);
        AppSettingsStore.Save(App.Settings);
    }

    public async Task ApplySystemLanguage()
    {
        App.Settings.Language = LanguagePreference.System;
        this._languagePreference = LanguagePreference.System;
        await this.UpdateLanguage((AppLanguage)((int)App.Settings.ResolvedLanguage - 1));
        AppSettingsStore.Save(App.Settings);
    }

    private async Task UpdateLanguage(AppLanguage language)
    {
        await Localizer.Get().SetLanguage(GetLanguageTag(language));
        this._currentLanguage = language;
        this.UpdateLanguageCode();

        if (this.ContentFrame.Content is Settings settingsPage)
        {
            settingsPage.UpdateLanguageSelection(App.Settings.Language, language);
        }
    }

    private void UpdateLanguageCode()
    {
        this.LanguageCode.Glyph = this._languagePreference switch
        {
            LanguagePreference.System => "AUTO",
            LanguagePreference.English => "EN",
            LanguagePreference.Greek => "ΕΛ",
            _ => "EN"
        };
        this.UpdateLanguageTooltip();
    }

    private void UpdateLanguageTooltip()
    {
        string resolvedName = this._currentLanguage == AppLanguage.English ? "English" : "Greek";
        string tooltip = this._languagePreference switch
        {
            LanguagePreference.System => $"Language: Follow Windows ({resolvedName})",
            LanguagePreference.English => "Language: English",
            LanguagePreference.Greek => "Language: Greek",
            _ => string.Empty
        };
        this.LanguageTooltip = tooltip;
        ToolTipService.SetToolTip(this.LanguageToggleSideButton, tooltip);
        this.OnPropertyChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    internal static string GetLanguageTag(AppLanguage language)
    {
        return language switch
        {
            AppLanguage.Greek => "el-GR",
            _ => "en-US"
        };
    }

    public void ApplyTheme(AppTheme theme)
    {
        this._currentTheme = theme;
        App.Settings.Theme = (ThemePreference)theme;
        AppSettingsStore.Save(App.Settings);
        ElementTheme requestedTheme = this._currentTheme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        if (this.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = requestedTheme;
        }

        this.AppWindow.TitleBar.ButtonForegroundColor = requestedTheme == ElementTheme.Dark
            ? Colors.White
            : null;
        this.AppWindow.TitleBar.ButtonInactiveForegroundColor = requestedTheme == ElementTheme.Dark
            ? Colors.LightGray
            : null;

        this.ThemeIcon.Glyph = this._currentTheme switch
        {
            AppTheme.Default => "\uE7F4",
            AppTheme.Light => "\uE706",
            AppTheme.Dark => "\uE708",
            _ => "\uE7F4"
        };

        if (this.ContentFrame.Content is Settings settingsPage)
        {
            settingsPage.UpdateThemeSelection(theme);
        }
    }
}