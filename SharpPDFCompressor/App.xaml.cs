using Microsoft.UI.Xaml;
using Windows.Globalization;

namespace SharpPDFCompressor;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
#if DEBUG
        //Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "el-GR";
        ApplicationLanguages.PrimaryLanguageOverride = "en-US";
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