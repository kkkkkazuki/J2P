using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace J2P.Core.Pdf;

/// <summary>複数PDFの結合。</summary>
public static class PdfMerger
{
    /// <summary>PDFバイト列を順に結合して output へ保存する。</summary>
    public static void Merge(IEnumerable<byte[]> pdfBlobs, Stream output)
    {
        using var merged = new PdfDocument();
        foreach (var blob in pdfBlobs)
        {
            using var ms = new MemoryStream(blob);
            using var src = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            foreach (var page in src.Pages)
                merged.AddPage(page);
        }
        merged.Save(output);
    }

    /// <summary>PDFファイル群を順に結合して outputPath へ保存する。</summary>
    public static void MergeFiles(IEnumerable<string> inputPaths, string outputPath)
    {
        using var merged = new PdfDocument();
        foreach (var path in inputPaths)
        {
            using var src = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            foreach (var page in src.Pages)
                merged.AddPage(page);
        }
        using var fs = File.Create(outputPath);
        merged.Save(fs);
    }
}
