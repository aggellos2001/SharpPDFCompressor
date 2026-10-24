using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SharpPDFCompressor.Ui;
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

    private AppTheme _currentTheme = AppTheme.Default;
    private object? _selectionBeforeThemeToggle;

    public AppTheme CurrentTheme => this._currentTheme;

    public MainWindow()
    {
        this.InitializeComponent();
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
        if (args.SelectedItemContainer?.Name == "ThemeToggleSideButton")
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

    public void ApplyTheme(AppTheme theme)
    {
        this._currentTheme = theme;
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