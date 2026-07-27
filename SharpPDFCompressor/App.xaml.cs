using Microsoft.UI.Xaml;
using System;

namespace SharpPDFCompressor;
public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    private Window? _window;

    public App()
    {
#if DEBUG
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "el-GR";
#endif
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainWindow = _window;
    }
}