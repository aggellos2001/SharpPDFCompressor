using System.IO;
using System.Threading.Tasks;

namespace SharpPDFCompressor.ViewModels;

public sealed class FileCompressor : Compressor
{
    private string? _compressedFilePath;
    /// <summary>
    ///     Creates a compressor class for single PDF files.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException">
    ///     Thrown when file is not a PDF file or does not exist.
    /// </exception>
    protected override async Task<CompressionResult> PreCompressAsync()
    {
        CompressionResult result = new();
        if (!File.Exists(this.InputFilesPath) || !Path.GetExtension(this.InputFilesPath).ToLower().EndsWith("pdf"))
        {
            result.Errors.Add("File not found!");
            return result;
        }

        this.Files = [this.InputFilesPath];
        this.PdfFilesCount = 1;
        this.BeforeSizeBytes = new FileInfo(this.InputFilesPath).Length;

        return result;
    }

    protected override CompressionResult PostFileCompress(string originalFilePath, string compressedFilePath)
    {
        this._compressedFilePath = compressedFilePath;
        if (!this.DeleteOriginalFiles)
        {
            return new CompressionResult();
        }

        // if delete original files is enabled we remove the original file and rename the
        // newly created one.
        File.Delete(originalFilePath);
        File.Move(compressedFilePath, originalFilePath);

        return new CompressionResult();
    }

    protected override async Task<CompressionResult> PostCompressionAsync()
    {
        string? outputPath = this.DeleteOriginalFiles ? this.InputFilesPath : this._compressedFilePath;
        if (outputPath is not null && File.Exists(outputPath))
        {
            this.AfterSizeBytes = new FileInfo(outputPath).Length;
        }

        return new CompressionResult();
    }
}