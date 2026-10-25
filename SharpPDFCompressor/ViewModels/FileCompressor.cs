using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SharpPDFCompressor.ViewModels;

public sealed class FileCompressor : Compressor
{
    public IReadOnlyList<string>? InputFilePaths { get; init; }

    /// <summary>
    ///     Creates a compressor class for PDF files.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException">
    ///     Thrown when file is not a PDF file or does not exist.
    /// </exception>
    protected override async Task<CompressionResult> PreCompressAsync()
    {
        CompressionResult result = new();
        List<string> inputFiles = this.InputFilePaths is { Count: > 0 }
            ? [.. this.InputFilePaths]
            : [this.InputFilesPath];

        foreach (string inputFile in inputFiles)
        {
            if (!File.Exists(inputFile) || !IsPdf(inputFile))
            {
                result.Errors.Add("File not found!");
                return result;
            }
        }

        this.Files = inputFiles;
        this.PdfFilesCount = inputFiles.Count;
        this.BeforeSizeBytes = GetFilesSize(inputFiles);

        return result;
    }

    protected override CompressionResult PostFileCompress(string originalFilePath, string compressedFilePath)
    {
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

    protected override Task<CompressionResult> PostCompressionAsync() => Task.FromResult(new CompressionResult());
}