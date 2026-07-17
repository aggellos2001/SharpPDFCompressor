using SharpPDFCompressor.ViewModels;


namespace SharpPDFCompressor.Ui;

public partial class ProgressDialog
{
    public ProgressDialogViewModel ViewModel { get; }

    public ProgressDialog(ProgressDialogViewModel viewModel)
    {
        InitializeComponent();
        this.ViewModel = viewModel;
    }
}