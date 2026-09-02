using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SharpPDFCompressor.ViewModels;

public sealed class FileCompressor : Compressor
{
    public FileCompressor()
    {
        //todo !
        this.Files = Enumerable.Empty<string>();
    }

    protected override IEnumerable<string> Files { get; init; }

    protected override async Task<CompressionResult> PreCompressAsync()
    {
        throw new NotImplementedException();
    }

    protected override async Task<CompressionResult> PostCompressAsync()
    {
        throw new NotImplementedException();
    }
}