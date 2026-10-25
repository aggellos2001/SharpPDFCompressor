using Microsoft.UI.Xaml;
using SharpPDFCompressor.DataModels;
using System;
using System.IO;
using WinUI3Localizer;

namespace SharpPDFCompressor;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        this.InitializeComponent();
    }

    public static Window? MainWindow { get; private set; }
    internal static AppSettings Settings { get; private set; } = new();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Settings = AppSettingsStore.Load();
        await new LocalizerBuilder()
            .AddStringResourcesFolderForLanguageDictionaries(Path.Combine(AppContext.BaseDirectory, "Strings"))
            .SetOptions(options => options.DefaultLanguage = Settings.ResolvedLanguage == LanguagePreference.Greek
                ? "el-GR"
                : "en-US")
            .Build();

        this._window = new MainWindow();
        MainWindow = this._window;
        this._window.Activate();
    }
}