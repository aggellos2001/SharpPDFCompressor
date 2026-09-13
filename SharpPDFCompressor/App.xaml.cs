using Microsoft.UI.Xaml;

namespace SharpPDFCompressor;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
#if DEBUG
        // ApplicationLanguages.PrimaryLanguageOverride = "en-US";
        // ApplicationLanguages.PrimaryLanguageOverride = "el-GR";
#endif
        this.InitializeComponent();
    }

    public static Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        this._window = new MainWindow();
        MainWindow = this._window;
    }
}