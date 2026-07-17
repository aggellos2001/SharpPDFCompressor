using Microsoft.UI.Xaml.Controls;
using SharpPDFCompressor.ViewModels;
using System;
using System.IO;

namespace SharpPDFCompressor.Ui;

public sealed partial class Home : Page
{
    private static readonly string DllPath = Path.Combine(AppContext.BaseDirectory, "Runtimes", "gsdll64.dll");

    public HomeViewModel ViewModel { get; } = new();

    public Home()
    {
        InitializeComponent();
    }
}