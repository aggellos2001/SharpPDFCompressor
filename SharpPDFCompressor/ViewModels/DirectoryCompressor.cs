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
            IEnumerable<string> files =
                Directory.EnumerateFiles(this.InputFilesPath, "*.*", SearchOption.AllDirectories);

            string sourceRoot = this.InputFilesPath;
            AppUtils.GetTempDir(out string tempDir);
            string targetRoot = Path.Combine(tempDir, Path.GetRandomFileName());

            this._tempFolderLocation = targetRoot;

            if (!Directory.Exists(targetRoot))
            {
                Directory.CreateDirectory(targetRoot);
            }

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

            this.Files = [.. Directory.EnumerateFiles(targetRoot, "*.*", SearchOption.AllDirectories)];
        }
        else
        {
            this.Files = [.. Directory.EnumerateFiles(this.InputFilesPath, "*.*", SearchOption.AllDirectories)];
        }

        this.PdfFilesCount = this.Files.Count(file => file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

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

        return result;
    }
}