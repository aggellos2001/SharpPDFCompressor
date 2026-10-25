using SharpPDFCompressor.ViewModels;

namespace SharpPDFCompressor.Ui;

public partial class ProgressDialog
{
    public ProgressDialog(ProgressDialogViewModel viewModel)
    {
        this.ViewModel = viewModel;
        this.InitializeComponent();
    }

    public ProgressDialogViewModel ViewModel { get; }
}