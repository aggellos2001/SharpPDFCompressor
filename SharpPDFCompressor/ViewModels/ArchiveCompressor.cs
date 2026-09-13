using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPDFCompressor.ViewModels;

public class ArchiveCompressor : Compressor
{
    private string? _tempFolderLocation;

    protected override async Task<CompressionResult> PreCompressAsync()
    {
        CompressionResult result = new();
        string? parentDir = Path.GetDirectoryName(this.InputFilesPath);
        if (parentDir is null)
        {
            result.Errors.Add(this.ResourceLoader.GetString("GenericError"));
            return result;
        }

        // first we extract the archive in the temp folder
        // AppUtils.GetTempDir(out string tempDir);

        string zipExtractionDir = Path.Combine(parentDir, Path.GetRandomFileName());

        if (!Directory.Exists(zipExtractionDir))
        {
            Directory.CreateDirectory(zipExtractionDir);
        }

        try
        {
            await Task.Run(() =>
            {
                using IArchive archive = ArchiveFactory.OpenArchive(this.InputFilesPath);
                ExtractionOptions options = new()
                {
                    ExtractFullPath = true,
                    PreserveFileTime = true,
                    Overwrite = true
                };
                foreach (IArchiveEntry entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    this.Ct.ThrowIfCancellationRequested();
                    entry.WriteToDirectory(zipExtractionDir, options);
                }
            }, this.Ct);
        }
        catch (OperationCanceledException e)
        {
            result.Errors.Add(e.Message);
        }
        catch (Exception e)
        {
            result.Errors.Add("Error occured " + e.Message);
        }

        // keep the temp folder reference for cleanup later
        this._tempFolderLocation = zipExtractionDir;

        //set the files to the temporary folder where the archive is extracted
        this.Files = Directory.GetFiles(zipExtractionDir, "*.*", SearchOption.AllDirectories);

        this.PdfFilesCount = this.Files.Count(file => file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

        return result;
    }

    protected override CompressionResult PostFileCompress(string originalFilePath, string compressedFilePath)
    {
        throw new System.NotImplementedException();
    }

    protected override async Task<CompressionResult> PostCompressionAsync()
    {
        CompressionResult result = new();

        if (this._tempFolderLocation is null)
        {
            return result;
        }

        // get all files in the temp directory
        IEnumerable<string> files =
            Directory.EnumerateFiles(this._tempFolderLocation, "*.*", SearchOption.AllDirectories);

        // // specify the root directory to output the compressed archive
        // string targetRootDirectory = this.InputFilesPath
        //     .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "_compressed";

        try
        {
            string? resultDir = Path.GetDirectoryName(this.InputFilesPath);
            if (resultDir == null)
            {
                result.Errors.Add(this.ResourceLoader.GetString("GenericError"));
                return result;
            }

            string originalArchiveName = Path.GetFileNameWithoutExtension(this.InputFilesPath);
            string destName = Path.Combine(resultDir, $"{originalArchiveName}{CompressedSuffix}.zip");
            int counter = 1;
            while (File.Exists(destName))
            {
                destName = Path.Combine(resultDir, $"{originalArchiveName}{CompressedSuffix}({counter}).zip");
                counter++;
            }

            /*write a new archive with the files from the temp folder to the original
            location where the archive existed
             */
            await using FileStream stream = File.Create(destName);
            await using IAsyncWriter writer = await WriterFactory
                .OpenAsyncWriter(stream, ArchiveType.Zip,
                    new WriterOptions(CompressionType.Deflate)
                    {
                        ArchiveEncoding = new ArchiveEncoding { Forced = Encoding.UTF8 }
                    }, this.Ct);

            await writer.WriteAllAsync(this._tempFolderLocation, "*", SearchOption.AllDirectories, this.Ct);
        }
        catch (Exception e)
        {
            result.Errors.Add(e.Message);
        }
        finally
        {
            // finally cleanup the temp directory
            if (Directory.Exists(this._tempFolderLocation))
            {
                Directory.Delete(this._tempFolderLocation, true);
            }
        }


        return result;
    }
}