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
            string[] files = [.. Directory.EnumerateFiles(AppUtils.NormalizePath(this.InputFilesPath), "*.*", SearchOption.AllDirectories)];

            string sourceRoot = AppUtils.NormalizePath(this.InputFilesPath);
            AppUtils.GetTempDir(out string tempDir);
            string targetRoot = Path.Combine(tempDir, Path.GetRandomFileName());

            this._tempFolderLocation = targetRoot;

            if (!Directory.Exists(AppUtils.NormalizePath(targetRoot)))
            {
                Directory.CreateDirectory(AppUtils.NormalizePath(targetRoot));
            }

            // Copy all directories (including empty ones) to preserve folder structure
            string[] sourceDirectories = [.. Directory.EnumerateDirectories(AppUtils.NormalizePath(sourceRoot), "*", SearchOption.AllDirectories)];
            foreach (string directory in sourceDirectories)
            {
                string relativePath = Path.GetRelativePath(sourceRoot, directory);
                string targetDirectory = Path.Combine(targetRoot, relativePath);
                Directory.CreateDirectory(AppUtils.NormalizePath(targetDirectory));
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
                        Directory.CreateDirectory(AppUtils.NormalizePath(targetDirectory));
                    }

                    File.Copy(AppUtils.NormalizePath(file), AppUtils.NormalizePath(targetFilePath), false);
                });
            }, this.Ct);

            this.Files = [.. Directory.EnumerateFiles(AppUtils.NormalizePath(targetRoot), "*.*", SearchOption.AllDirectories)
                .Where(file => IsPdf(file) && (!this.IgnoreCompressedFiles ||
                    !Path.GetFileNameWithoutExtension(file).Contains(this.CompressedSuffix, StringComparison.OrdinalIgnoreCase)))];
        }
        else
        {
            string[] files = [.. Directory.EnumerateFiles(AppUtils.NormalizePath(this.InputFilesPath), "*.*", SearchOption.AllDirectories)];
            this.Files = [.. files.Where(file => IsPdf(file) && (!this.IgnoreCompressedFiles ||
                !Path.GetFileNameWithoutExtension(file).Contains(this.CompressedSuffix, StringComparison.OrdinalIgnoreCase)))];
        }

        // Calculate BeforeSizeBytes only from the PDF files that will be compressed
        this.BeforeSizeBytes = GetFilesSize(this.Files);

        this.PdfFilesCount = this.Files.Count();

        return result;
    }


    protected override void OnFileCompressFailed(string file)
    {
        if (!this.DeleteOriginalFiles)
        {
            return;
        }

        try
        {
            File.Delete(AppUtils.NormalizePath(file));
        }
        catch
        {
            // Best-effort: omit the unreadable original from the _compressed output.
        }
    }

    protected override CompressionResult PostFileCompress(string originalFilePath, string compressedFilePath)
    {
        if (!this.DeleteOriginalFiles)
        {
            return new CompressionResult();
        }

        File.Delete(AppUtils.NormalizePath(originalFilePath));
        File.Move(AppUtils.NormalizePath(compressedFilePath), AppUtils.NormalizePath(originalFilePath));

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
                Directory.EnumerateFiles(AppUtils.NormalizePath(this._tempFolderLocation), "*.*", SearchOption.AllDirectories);

            int counter = 1;
            string targetRootDirectory = this.InputFilesPath
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "_compressed";
            while (Directory.Exists(AppUtils.NormalizePath(targetRootDirectory)))
            {
                targetRootDirectory = this.InputFilesPath
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + $"_compressed ({counter++})";
            }

            // Copy all directories (including empty ones) to preserve folder structure
            string[] tempDirectories = [.. Directory.EnumerateDirectories(AppUtils.NormalizePath(this._tempFolderLocation), "*", SearchOption.AllDirectories)];
            foreach (string directory in tempDirectories)
            {
                string relativePath = Path.GetRelativePath(this._tempFolderLocation, directory);
                string targetDirectory = Path.Combine(targetRootDirectory, relativePath);
                Directory.CreateDirectory(AppUtils.NormalizePath(targetDirectory));
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
                        Directory.CreateDirectory(AppUtils.NormalizePath(targetDir));
                    }

                    File.Copy(AppUtils.NormalizePath(file), AppUtils.NormalizePath(targetFile), false);
                });
            }, this.Ct);
        }
        finally
        {
            // cleanup the temp directory
            if (Directory.Exists(AppUtils.NormalizePath(this._tempFolderLocation)))
            {
                Directory.Delete(AppUtils.NormalizePath(this._tempFolderLocation), true);
            }
        }

        return result;
    }
}