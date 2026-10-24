using SharpPDFCompressor.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SharpPDFCompressor.ViewModels;

public class DirectoryCompressor : Compressor
{
    private string? _tempFolderLocation;

    protected override async Task<CompressionResult> PreCompressAsync()
    {
        CompressionResult result = new();

        if (this.DeleteOriginalFiles)
        {
            string[] files = [.. Directory.EnumerateFiles(this.InputFilesPath, "*.*", SearchOption.AllDirectories)];

            string sourceRoot = this.InputFilesPath;
            AppUtils.GetTempDir(out string tempDir);
            string targetRoot = Path.Combine(tempDir, Path.GetRandomFileName());

            this._tempFolderLocation = targetRoot;

            if (!Directory.Exists(targetRoot))
            {
                Directory.CreateDirectory(targetRoot);
            }

            // Copy files asynchronously to avoid blocking the UI
            await Task.Run(() =>
            {
                Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, file =>
                {
                    string relativePath = Path.GetRelativePath(sourceRoot, file);
                    string targetFilePath = Path.Combine(targetRoot, relativePath);
                    string? targetDirectory = Path.GetDirectoryName(targetFilePath);
                    if (!string.IsNullOrWhiteSpace(targetDirectory))
                    {
                        Directory.CreateDirectory(targetDirectory);
                    }

                    File.Copy(file, targetFilePath, false);
                });
            }, this.Ct);

            this.Files = [.. Directory.EnumerateFiles(targetRoot, "*.*", SearchOption.AllDirectories)
                .Where(file => IsPdf(file) && (!this.IgnoreCompressedFiles ||
                    !Path.GetFileNameWithoutExtension(file).Contains(this.CompressedSuffix, StringComparison.OrdinalIgnoreCase)))];
        }
        else
        {
            string[] files = [.. Directory.EnumerateFiles(this.InputFilesPath, "*.*", SearchOption.AllDirectories)];
            this.Files = [.. files.Where(file => IsPdf(file) && (!this.IgnoreCompressedFiles ||
                !Path.GetFileNameWithoutExtension(file).Contains(this.CompressedSuffix, StringComparison.OrdinalIgnoreCase)))];
        }

        // Calculate BeforeSizeBytes only from the PDF files that will be compressed
        this.BeforeSizeBytes = GetFilesSize(this.Files);

        this.PdfFilesCount = this.Files.Count();

        return result;
    }


    protected override CompressionResult PostFileCompress(string originalFilePath, string compressedFilePath)
    {
        if (!this.DeleteOriginalFiles)
        {
            return new CompressionResult();
        }

        File.Delete(originalFilePath);
        File.Move(compressedFilePath, originalFilePath);

        return new CompressionResult();
    }

    protected override async Task<CompressionResult> PostCompressionAsync()
    {
        CompressionResult result = new();

        if (!this.DeleteOriginalFiles || this._tempFolderLocation == null)
        {
            return result;
        }

        try
        {
            IEnumerable<string> files =
                Directory.EnumerateFiles(this._tempFolderLocation, "*.*", SearchOption.AllDirectories);

            int counter = 1;
            string targetRootDirectory = this.InputFilesPath
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "_compressed";
            while (Directory.Exists(targetRootDirectory))
            {
                targetRootDirectory = this.InputFilesPath
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + $"_compressed ({counter++})";
            }

            // Copy files asynchronously to avoid blocking the UI
            await Task.Run(() =>
            {
                Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, file =>
                {
                    string relativePath = Path.GetRelativePath(this._tempFolderLocation, file);
                    string targetFile = Path.Combine(targetRootDirectory, relativePath);

                    string? targetDir = Path.GetDirectoryName(targetFile);
                    if (!string.IsNullOrWhiteSpace(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Copy(file, targetFile, false);
                });
            }, this.Ct);
        }
        finally
        {
            // cleanup the temp directory
            if (Directory.Exists(this._tempFolderLocation))
            {
                Directory.Delete(this._tempFolderLocation, true);
            }
        }

        return result;
    }
}