using SharpPDFCompressor.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpPDFCompressor.ViewModels;

public class DirectoryCompressor : Compressor
{
    private string? _targetRootLocation;

    protected override async Task<CompressionResult> PreCompressAsync()
    {
        CompressionResult result = new();
        string sourceRoot = AppUtils.NormalizePath(this.InputFilesPath);

        await Task.Run(() =>
        {
            List<string> sourceFiles = [];
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
            {
                this.Ct.ThrowIfCancellationRequested();
                sourceFiles.Add(file);
            }

            List<string> pdfFiles = [];
            foreach (string file in sourceFiles)
            {
                this.Ct.ThrowIfCancellationRequested();
                if (IsPdf(file) && (!this.IgnoreCompressedFiles ||
                    !Path.GetFileNameWithoutExtension(file).Contains(this.CompressedSuffix, StringComparison.OrdinalIgnoreCase)))
                {
                    pdfFiles.Add(file);
                }
            }

            this.Files = pdfFiles;
            this.PdfFilesCount = pdfFiles.Count;

            long beforeSizeBytes = 0;
            IEnumerable<string> filesToMeasure = this.DeleteOriginalFiles ? sourceFiles : pdfFiles;
            foreach (string file in filesToMeasure)
            {
                this.Ct.ThrowIfCancellationRequested();
                beforeSizeBytes += new FileInfo(AppUtils.NormalizePath(file)).Length;
            }

            this.BeforeSizeBytes = beforeSizeBytes;

            if (!this.DeleteOriginalFiles)
            {
                return;
            }

            string targetRootDirectory = this.InputFilesPath
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "_compressed";
            int counter = 1;
            while (Directory.Exists(AppUtils.NormalizePath(targetRootDirectory)))
            {
                this.Ct.ThrowIfCancellationRequested();
                targetRootDirectory = this.InputFilesPath
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + $"_compressed ({counter++})";
            }

            this._targetRootLocation = targetRootDirectory;
            Directory.CreateDirectory(AppUtils.NormalizePath(targetRootDirectory));

            // Copy all directories (including empty ones) to preserve folder structure.
            foreach (string directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                this.Ct.ThrowIfCancellationRequested();
                string relativePath = Path.GetRelativePath(sourceRoot, directory);
                string targetDirectory = Path.Combine(targetRootDirectory, relativePath);
                Directory.CreateDirectory(AppUtils.NormalizePath(targetDirectory));
            }

            string[] nonPdfFiles = [.. sourceFiles.Where(file => !IsPdf(file))];
            long copiedNonPdfSizeBytes = 0;
            Parallel.ForEach(nonPdfFiles, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = this.Ct }, file =>
            {
                this.Ct.ThrowIfCancellationRequested();
                string relativePath = Path.GetRelativePath(sourceRoot, file);
                string targetFilePath = Path.Combine(targetRootDirectory, relativePath);
                string? targetDirectory = Path.GetDirectoryName(targetFilePath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(AppUtils.NormalizePath(targetDirectory));
                }

                File.Copy(AppUtils.NormalizePath(file), AppUtils.NormalizePath(targetFilePath), false);
                Interlocked.Add(ref copiedNonPdfSizeBytes,
                    new FileInfo(AppUtils.NormalizePath(targetFilePath)).Length);
            });
            this.AfterSizeBytes = copiedNonPdfSizeBytes;
        }, this.Ct);

        return result;
    }


    protected override string GetCompressedOutputPath(string inputFilePath, string compressedFileName)
    {
        if (!this.DeleteOriginalFiles)
        {
            return base.GetCompressedOutputPath(inputFilePath, compressedFileName);
        }

        string sourceRoot = AppUtils.NormalizePath(this.InputFilesPath);
        string relativePath = Path.GetRelativePath(sourceRoot, inputFilePath);
        string? relativeDirectory = Path.GetDirectoryName(relativePath);
        return Path.Combine(this._targetRootLocation!, relativeDirectory ?? string.Empty, compressedFileName);
    }

    protected override CompressionResult PostFileCompress(string originalFilePath, string compressedFilePath)
    {
        return new CompressionResult();
    }

    protected override Task<CompressionResult> PostCompressionAsync() => Task.FromResult(new CompressionResult());
}