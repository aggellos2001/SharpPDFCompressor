using Microsoft.UI.Xaml.Controls;
using SharpPDFCompressor.ViewModels;
using System.Diagnostics.CodeAnalysis;

namespace SharpPDFCompressor.Ui;

public sealed partial class Home : Page
{
    public Home()
    {
        this.InitializeComponent();
    }

    private static HomeViewModel? _sharedViewModel;

    // ReSharper disable once MemberCanBeMadeStatic.Global
    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    public HomeViewModel ViewModel => _sharedViewModel ??= new HomeViewModel();

    private void ResetCompressedSuffix_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        this.ViewModel.CompressedSuffix = Compressor.DefaultCompressedSuffix;
    }
}