using Microsoft.UI.Xaml;

namespace SharpPDFCompressor;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    private Window? _window;

    public App()
    {
#if DEBUG
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "el-GR";
        //Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "en-US";
#endif
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainWindow = _window;
    }
}