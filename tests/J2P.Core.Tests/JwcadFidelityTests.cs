using System.Text;
using J2P.Core.Jww;
using J2P.Core.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace J2P.Core.Tests;

/// <summary>
/// Jw_cad 実機の印刷結果と突き合わせて確認した挙動を固定するテスト。
/// 期待値は Jw_cad（Microsoft Print to PDF 経由）の出力をベクタ単位で実測して得たもの。
/// </summary>
public class JwcadFidelityTests
{
    private static JwwDocument Build(Action<JwwFixtureBuilder> configure)
    {
        var b = new JwwFixtureBuilder();
        b.PrintOriginX = 0;
        b.PrintOriginY = 0;
        configure(b);
        return JwwReader.Read(b.BuildStream());
    }

    private static string Content(JwwDocument doc)
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

    /// <summary>座標を並び順に取り出す（"x y m" / "x y l" の並び）。</summary>
    private static List<(double X, double Y)> PathPoints(string content)
    {
        var pts = new List<(double, double)>();
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(content, @"([-\d.]+) ([-\d.]+) (?:m|l)\b"))
            pts.Add((double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value)));
        return pts;
    }

    [Fact]
    public void 補助線色_線色9は印刷されない()
    {
        // Jw_cad の 線色9 は「補助線色」で、画面には出るが印刷はされない
        string withAux = Content(Build(b => b.AddLine(-100, 0, 100, 0, penColor: 9)));
        string empty = Content(Build(_ => { }));

        Assert.Equal(empty.Length, withAux.Length);
    }

    [Theory]
    [InlineData((ushort)1)]
    [InlineData((ushort)2)]
    [InlineData((ushort)8)]
    public void 線色1から8は印刷される(ushort penColor)
    {
        string content = Content(Build(b => b.AddLine(-100, 0, 100, 0, penColor: penColor)));
        string empty = Content(Build(_ => { }));

        Assert.True(content.Length > empty.Length, $"線色{penColor}が出力されていません");
    }

    [Fact]
    public void 補助線色は文字_円_点_ソリッドでも印刷されない()
    {
        string empty = Content(Build(_ => { }));

        Assert.Equal(empty.Length, Content(Build(b => b.AddText(0, 0, 40, 0, "補助", penColor: 9))).Length);
        Assert.Equal(empty.Length, Content(Build(b => b.AddArc(0, 0, 20, fullCircle: true, penColor: 9))).Length);
        Assert.Equal(empty.Length, Content(Build(b => b.AddPoint(0, 0, penColor: 9))).Length);
        Assert.Equal(empty.Length,
            Content(Build(b => b.AddSolid(0, 0, 20, 0, 0, 20, 20, 20, penColor: 9))).Length);
    }

    [Fact]
    public void ソリッドの頂点はファイルの並び順で結ぶ()
    {
        // Jw_cad は 4点を p0→p1→p2→p3 の順にそのまま結ぶ。
        // 下の並び（左下・右下・左上・右上）は交差するので蝶ネクタイ形になるのが正しい。
        var doc = Build(b => b.AddSolid(
            x0: -100, y0: -50,   // p0 左下
            x1: 100, y1: -50,    // p1 右下
            x2: -100, y2: 50,    // p2 左上
            x3: 100, y3: 50));   // p3 右上

        var pts = PathPoints(Content(doc));

        Assert.Equal(4, pts.Count);
        // 1点目と2点目は同じ高さ（下辺）、2→3で対角に上がる＝交差している
        Assert.Equal(pts[0].Y, pts[1].Y, 3);
        Assert.NotEqual(pts[1].Y, pts[2].Y, 3);
        Assert.Equal(pts[0].X, pts[2].X, 3);   // p0 と p2 は同じX（左端）
        Assert.Equal(pts[1].X, pts[3].X, 3);   // p1 と p3 は同じX（右端）
        Assert.Equal(pts[2].Y, pts[3].Y, 3);   // p2 と p3 は同じ高さ（上辺）
    }

    [Fact]
    public void 三角ソリッドは3点目と4点目が同じでも正しく描ける()
    {
        var doc = Build(b => b.AddSolid(-100, -50, 100, -50, 0, 50, 0, 50));

        var pts = PathPoints(Content(doc));

        Assert.Equal(4, pts.Count);
        Assert.Equal(pts[2].X, pts[3].X, 3);
        Assert.Equal(pts[2].Y, pts[3].Y, 3);
    }
}
