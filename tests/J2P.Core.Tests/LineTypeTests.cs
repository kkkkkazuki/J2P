using System.Text;
using J2P.Core.Jww;
using J2P.Core.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace J2P.Core.Tests;

/// <summary>
/// 線種（破線）の再現テスト。
/// 期待値は Jw_cad が実際に出力した PDF を実測して得たもの。
/// 例: 線種2（点線1、パターン0x99999999・unitDots=4・プリンタピッチ2）は
/// 線0.339mm / 空0.339mm・周期0.677mm で出力される。
/// </summary>
public class JwwLineTypesTests
{
    // 1ドット = プリンタピッチ2 × 1/300インチ
    private const double Bit = 2 * 25.4 / 300.0;   // ≒0.16933mm

    [Fact]
    public void 点線1_実測どおりの線と空になる()
    {
        var d = JwwLineTypes.Decode(0x99999999, unitDots: 4, printerPitch: 2);

        Assert.NotNull(d);
        Assert.Equal(new[] { 2 * Bit, 2 * Bit }, d!, new DoubleComparer(1e-9));
        // Jw_cad実測: 線0.339mm 空0.339mm
        Assert.Equal(0.339, d[0], 3);
        Assert.Equal(0.339, d[1], 3);
        Assert.Equal(0.677, d.Sum(), 3);   // 周期
    }

    [Theory]
    // 線種番号, パターン, unitDots, 期待周期のビット数, 期待する[線,空,…]のビット数
    [InlineData(2, 0x99999999u, 4u, new[] { 2, 2 })]                 // 点線1
    [InlineData(3, 0xC3C3C3C3u, 8u, new[] { 4, 4 })]                 // 点線2
    [InlineData(4, 0xE7E7E7E7u, 8u, new[] { 6, 2 })]                 // 点線3
    [InlineData(5, 0xF99FF99Fu, 16u, new[] { 10, 2, 2, 2 })]         // 一点鎖1
    [InlineData(6, 0xFFF99FFFu, 32u, new[] { 26, 2, 2, 2 })]         // 一点鎖2
    [InlineData(7, 0xF24FF24Fu, 16u, new[] { 8, 2, 1, 2, 1, 2 })]    // 二点鎖1
    [InlineData(8, 0xFFF24FFFu, 32u, new[] { 24, 2, 1, 2, 1, 2 })]   // 二点鎖2
    public void 標準線種_パターンが周期を保って展開される(
        int penStyle, uint pattern, uint unitDots, int[] expectedBits)
    {
        var d = JwwLineTypes.Decode(pattern, unitDots, printerPitch: 2);

        Assert.NotNull(d);
        Assert.Equal(expectedBits.Length, d!.Length);
        for (int i = 0; i < expectedBits.Length; i++)
            Assert.Equal(expectedBits[i] * Bit, d[i], 9);
        // 周期は必ず unitDots ビットぶん（末尾と先頭の結合で周期が伸びない）
        Assert.Equal(unitDots * Bit, d.Sum(), 9);
        Assert.True(d[0] > 0, $"線種{penStyle}は描画から始まること");
    }

    [Fact]
    public void 全ビット描画は実線として扱う()
    {
        Assert.Null(JwwLineTypes.Decode(0xFFFFFFFF, 8, 2));
        Assert.Null(JwwLineTypes.Decode(0xFFFFFFFF, 32, 2));
        Assert.Null(JwwLineTypes.Decode(0, 8, 2));
    }

    [Fact]
    public void 不正なunitDotsはnull()
    {
        Assert.Null(JwwLineTypes.Decode(0x99999999, 0, 2));
        Assert.Null(JwwLineTypes.Decode(0x99999999, 33, 2));
    }

    [Fact]
    public void プリンタピッチが長さに比例する()
    {
        var p2 = JwwLineTypes.Decode(0x99999999, 4, 2)!;
        var p10 = JwwLineTypes.Decode(0x99999999, 4, 10)!;
        Assert.Equal(p2[0] * 5, p10[0], 9);
    }

    private sealed class DoubleComparer(double tol) : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) <= tol;
        public int GetHashCode(double v) => 0;
    }
}

/// <summary>生成したPDFに破線指定が実際に出力されているかの回帰テスト。</summary>
public class DashOutputTests
{
    private static JwwDocument BuildDoc(byte penStyle)
    {
        var b = new JwwFixtureBuilder();
        b.PrintOriginX = 0;
        b.PrintOriginY = 0;
        // 実ファイルと同じ線種2（点線1）の定義
        b.LineTypePatterns[0] = 0x99999999;   // 線種2
        b.LineTypeUnitDots[0] = 4;
        b.LineTypePrinterPitches[0] = 2;
        b.AddLine(-100, 0, 100, 0, penStyle: penStyle);
        return JwwReader.Read(b.BuildStream());
    }

    private static string RenderContent(JwwDocument doc)
    {
        using var ms = new MemoryStream();
        PdfRenderer.RenderToStream(doc, new PdfRenderOptions(), ms);
        ms.Position = 0;
        using var pdf = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        var sb = new StringBuilder();
        foreach (var cs in pdf.Pages[0].Contents)
            sb.Append(Encoding.ASCII.GetString(cs.Stream.UnfilteredValue));
        return sb.ToString();
    }

    [Fact]
    public void 点線はPDFに破線オペレータとして出力される()
    {
        string content = RenderContent(BuildDoc(penStyle: 2));

        // PDFの破線指定は "[a b] 0 d"
        Assert.Contains(" d\n", content.Replace("\r", "\n"));
        int i = content.IndexOf('[');
        Assert.True(i >= 0, "破線配列が出力されていません: " + content);
        string arr = content.Substring(i, content.IndexOf(']', i) - i + 1);

        // PDFの破線配列はポイント単位。Jw_cad実測の 線0.339mm / 空0.339mm になること
        var mm = arr.Trim('[', ']').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.Parse(s) * 25.4 / 72).ToArray();
        Assert.Equal(2, mm.Length);
        Assert.Equal(0.339, mm[0], 2);
        Assert.Equal(0.339, mm[1], 2);
    }

    [Fact]
    public void 実線はPDFに破線オペレータを出さない()
    {
        string content = RenderContent(BuildDoc(penStyle: 1));
        Assert.DoesNotContain("] 0 d", content);
    }
}
