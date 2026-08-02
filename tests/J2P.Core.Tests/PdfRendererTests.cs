using J2P.Core.Jww;
using J2P.Core.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace J2P.Core.Tests;

public class PdfRendererTests
{
    private const double PtPerMm = 72.0 / 25.4;

    private static JwwDocument BuildDoc(Action<JwwFixtureBuilder>? configure = null)
    {
        var builder = new JwwFixtureBuilder(); // A3 (420x297)
        configure?.Invoke(builder);
        return JwwReader.Read(builder.BuildStream());
    }

    [Fact]
    public void Layout_印刷範囲の基準点が既定では枠中心になる()
    {
        // 印刷原点(0,0)・倍率1.0 は Jw_cad で「用紙を図面原点中心に置いた」状態。
        // 実機の Jw_cad 出力と突き合わせて確認した既定の解釈。
        var doc = BuildDoc(b =>
        {
            b.PrintOriginX = 0;
            b.PrintOriginY = 0;
            b.PrintScale = 1.0;
            b.AddLine(0, 0, 10, 10);
        });
        var options = new PdfRenderOptions { PrintArea = PrintAreaMode.FilePrintSettings };

        var layout = PdfRenderer.ComputeLayout(doc, options, new List<string>());

        Assert.Equal(420 * PtPerMm, layout.PageWidthPt, 3);
        Assert.Equal(297 * PtPerMm, layout.PageHeightPt, 3);
        // 基準点(0,0) がページ中心
        var (cx, cy) = layout.Transform.Map(0, 0);
        Assert.Equal(layout.PageWidthPt / 2, cx, 3);
        Assert.Equal(layout.PageHeightPt / 2, cy, 3);
        // 枠の左下(-210,-148.5) がページ左下
        var (px, py) = layout.Transform.Map(-210, -148.5);
        Assert.Equal(0, px, 3);
        Assert.Equal(layout.PageHeightPt, py, 3);
    }

    [Fact]
    public void Layout_印刷範囲の中心はずれない_原点が移動しても()
    {
        // 印刷範囲を用紙中心から移動したケース: 基準点(-100, -50)
        var doc = BuildDoc(b =>
        {
            b.PrintOriginX = -100;
            b.PrintOriginY = -50;
            b.PrintScale = 1.0;
            b.AddLine(0, 0, 10, 10);
        });
        var options = new PdfRenderOptions { PrintArea = PrintAreaMode.FilePrintSettings };

        var layout = PdfRenderer.ComputeLayout(doc, options, new List<string>());

        // 基準点そのものがページ中心に写る（＝設定した印刷範囲の中心がずれない）
        var (cx, cy) = layout.Transform.Map(-100, -50);
        Assert.Equal(layout.PageWidthPt / 2, cx, 3);
        Assert.Equal(layout.PageHeightPt / 2, cy, 3);
    }

    [Theory]
    // 基準点位置コード, 基準点がページ上のどこに来るか(0..1 の相対位置)
    [InlineData(0u, 0.5, 0.5)]   // 無指定 → 中心
    [InlineData(5u, 0.5, 0.5)]   // 中中
    [InlineData(1u, 0.0, 1.0)]   // 左下
    [InlineData(9u, 1.0, 0.0)]   // 右上
    [InlineData(7u, 0.0, 0.0)]   // 左上
    [InlineData(3u, 1.0, 1.0)]   // 右下
    public void Layout_基準点位置コードが枠の位置を決める(uint basePos, double relX, double relY)
    {
        var doc = BuildDoc(b =>
        {
            b.PrintOriginX = 30;
            b.PrintOriginY = -20;
            b.PrintScale = 1.0;
            b.PrintFlags = basePos * 10;   // 十位が基準点位置
            b.AddLine(0, 0, 10, 10);
        });
        var options = new PdfRenderOptions { PrintArea = PrintAreaMode.FilePrintSettings };

        var layout = PdfRenderer.ComputeLayout(doc, options, new List<string>());

        var (px, py) = layout.Transform.Map(30, -20);
        Assert.Equal(layout.PageWidthPt * relX, px, 3);
        Assert.Equal(layout.PageHeightPt * relY, py, 3);
    }

    [Fact]
    public void Layout_ファイルの印刷倍率50パーセントを反映する()
    {
        var doc = BuildDoc(b =>
        {
            b.PrintOriginX = 0;
            b.PrintOriginY = 0;
            b.PrintScale = 0.5;
            b.AddLine(0, 0, 10, 10);
        });
        var options = new PdfRenderOptions { PrintArea = PrintAreaMode.FilePrintSettings };

        var layout = PdfRenderer.ComputeLayout(doc, options, new List<string>());

        Assert.Equal(PtPerMm * 0.5, layout.Transform.Scale, 6);
        // 倍率50%なら枠は 840x594mm、その左下(-420,-297)がページ左下
        var (px, py) = layout.Transform.Map(-420, -297);
        Assert.Equal(0, px, 3);
        Assert.Equal(layout.PageHeightPt, py, 3);
    }

    [Fact]
    public void Layout_90度回転出力で縦ページになる()
    {
        var doc = BuildDoc(b =>
        {
            b.PrintOriginX = 0;
            b.PrintOriginY = 0;
            b.PrintScale = 1.0;
            b.PrintFlags = 1; // 90°回転
            b.AddLine(0, 0, 10, 10);
        });
        var options = new PdfRenderOptions { PrintArea = PrintAreaMode.FilePrintSettings };

        var layout = PdfRenderer.ComputeLayout(doc, options, new List<string>());

        Assert.Equal(297 * PtPerMm, layout.PageWidthPt, 3);
        Assert.Equal(420 * PtPerMm, layout.PageHeightPt, 3);
        Assert.True(layout.Transform.Rotate90);
    }

    [Fact]
    public void Layout_印刷倍率が異常なら用紙全体へフォールバック()
    {
        var doc = BuildDoc(b =>
        {
            b.PrintScale = 0;   // 壊れた印刷設定
            b.AddLine(-200, -140, -100, -100);
        });
        var options = new PdfRenderOptions { PrintArea = PrintAreaMode.FilePrintSettings };
        var warnings = new List<string>();

        var layout = PdfRenderer.ComputeLayout(doc, options, warnings);

        Assert.Contains(warnings, w => w.Contains("用紙全体"));
        var (cx, cy) = layout.Transform.Map(0, 0);
        Assert.Equal(layout.PageWidthPt / 2, cx, 3);
        Assert.Equal(layout.PageHeightPt / 2, cy, 3);
    }

    [Fact]
    public void Layout_A3図面をA4へ自動縮小できる()
    {
        var doc = BuildDoc(b => b.AddLine(0, 0, 10, 10));
        var options = new PdfRenderOptions
        {
            PrintArea = PrintAreaMode.PaperFull,
            Paper = PaperSelection.A4,
        };

        var layout = PdfRenderer.ComputeLayout(doc, options, new List<string>());

        // A3(420x297) → A4(297x210): 幅比297/420と高さ比210/297の小さい方
        Assert.Equal(297 * PtPerMm, layout.PageWidthPt, 3);
        Assert.Equal(PtPerMm * (210.0 / 297.0), layout.Transform.Scale, 6);
    }

    [Fact]
    public void Layout_倍率明示指定はフィットより優先される()
    {
        var doc = BuildDoc(b => b.AddLine(0, 0, 10, 10));
        var options = new PdfRenderOptions
        {
            PrintArea = PrintAreaMode.PaperFull,
            Magnification = 0.7,
        };

        var layout = PdfRenderer.ComputeLayout(doc, options, new List<string>());

        Assert.Equal(PtPerMm * 0.7, layout.Transform.Scale, 6);
    }

    [Fact]
    public void Render_全図形入りの図面をPDF化して再オープンできる()
    {
        var doc = BuildDoc(b =>
        {
            b.PrintOriginX = 0;
            b.PrintOriginY = 0;
            b.AddLine(0, 0, 100, 50)
             .AddLine(-50, -50, 50, 50, penStyle: 5)   // 一点鎖線
             .AddArc(50, 50, 25, flatness: 0.7, tilt: 0.3)
             .AddArc(0, 0, 30, fullCircle: true)
             .AddPoint(5, 5)
             .AddText(10, 10, 60, 10, "図面テスト 123 ABC あいう")
             .AddSolid(0, 0, 20, 0, 0, 20, 20, 20, rgb: 0x000080FF)
             .AddBlockInsert(100, 50, 1.5, 1.5, Math.PI / 4, 7)
             .AddBlockDefinitionWithLine(7, "部品", 0, 0, 30, 0);
        });

        using var ms = new MemoryStream();
        var warnings = PdfRenderer.RenderToStream(doc, new PdfRenderOptions(), ms);

        Assert.True(ms.Length > 1000);
        ms.Position = 0;
        using var reopened = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        Assert.Equal(1, reopened.PageCount);
        Assert.Equal(420 * PtPerMm, reopened.Pages[0].Width.Point, 1);
        Assert.Equal(297 * PtPerMm, reopened.Pages[0].Height.Point, 1);
    }

    [Fact]
    public void Render_白黒モードでも変換できる()
    {
        var doc = BuildDoc(b =>
        {
            b.PrintOriginX = 0;
            b.PrintOriginY = 0;
            b.AddLine(0, 0, 100, 50, penColor: 3);
            b.AddSolid(0, 0, 20, 0, 0, 20, 20, 20, rgb: 0x000080FF);
        });

        using var ms = new MemoryStream();
        PdfRenderer.RenderToStream(doc, new PdfRenderOptions { Color = ColorMode.BlackAndWhite }, ms);

        Assert.True(ms.Length > 500);
    }

    [Fact]
    public void Render_補助線種と仮点は出力されない()
    {
        var docWithAux = BuildDoc(b =>
        {
            b.PrintOriginX = 0;
            b.PrintOriginY = 0;
            b.AddLine(0, 0, 100, 50, penStyle: 9); // 補助線種
            b.AddPoint(5, 5, temporary: true);      // 仮点
        });
        var docEmpty = BuildDoc(b =>
        {
            b.PrintOriginX = 0;
            b.PrintOriginY = 0;
        });

        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        PdfRenderer.RenderToStream(docWithAux, new PdfRenderOptions(), ms1);
        PdfRenderer.RenderToStream(docEmpty, new PdfRenderOptions(), ms2);

        // 補助線・仮点のみの図面は空図面とほぼ同じサイズになる
        Assert.InRange(Math.Abs(ms1.Length - ms2.Length), 0, 200);
    }

    [Fact]
    public void Merge_複数PDFを1つに結合できる()
    {
        byte[] MakePdf()
        {
            var doc = BuildDoc(b =>
            {
                b.PrintOriginX = 0;
                b.PrintOriginY = 0;
                b.AddLine(0, 0, 100, 50);
            });
            using var ms = new MemoryStream();
            PdfRenderer.RenderToStream(doc, new PdfRenderOptions(), ms);
            return ms.ToArray();
        }

        using var merged = new MemoryStream();
        PdfMerger.Merge(new[] { MakePdf(), MakePdf(), MakePdf() }, merged);

        merged.Position = 0;
        using var reopened = PdfReader.Open(merged, PdfDocumentOpenMode.Import);
        Assert.Equal(3, reopened.PageCount);
    }
}
