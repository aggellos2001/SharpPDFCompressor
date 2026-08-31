using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        string? tag = args.SelectedItemContainer.Name;

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
}