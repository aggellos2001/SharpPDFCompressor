using SharpPDFCompressor.ViewModels;

namespace SharpPDFCompressor.Ui;

public partial class ProgressDialog
{
    public ProgressDialog(ProgressDialogViewModel viewModel)
    {
        this.InitializeComponent();
        this.ViewModel = viewModel;
    }

    public ProgressDialogViewModel ViewModel { get; }
}