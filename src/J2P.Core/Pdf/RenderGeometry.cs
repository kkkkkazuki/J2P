using J2P.Core.Jww;
using J2P.Core.Jww.Entities;

namespace J2P.Core.Pdf;

/// <summary>2次元アフィン変換（ブロック配置の合成用、図面座標系内）。</summary>
internal readonly struct Affine2D
{
    public readonly double M11, M12, M21, M22, Dx, Dy;

    public Affine2D(double m11, double m12, double m21, double m22, double dx, double dy)
    {
        M11 = m11; M12 = m12; M21 = m21; M22 = m22; Dx = dx; Dy = dy;
    }

    public static Affine2D Identity => new(1, 0, 0, 1, 0, 0);

    /// <summary>ブロック配置: 平行移動(bx,by) ∘ 回転(rot) ∘ 拡大(sx,sy)。</summary>
    public static Affine2D BlockInsert(double bx, double by, double sx, double sy, double rot)
    {
        double c = Math.Cos(rot), s = Math.Sin(rot);
        return new Affine2D(c * sx, -s * sy, s * sx, c * sy, bx, by);
    }

    /// <summary>this ∘ other（otherを先に適用）。</summary>
    public Affine2D Compose(in Affine2D o) => new(
        M11 * o.M11 + M12 * o.M21,
        M11 * o.M12 + M12 * o.M22,
        M21 * o.M11 + M22 * o.M21,
        M21 * o.M12 + M22 * o.M22,
        M11 * o.Dx + M12 * o.Dy + Dx,
        M21 * o.Dx + M22 * o.Dy + Dy);

    public (double X, double Y) Apply(double x, double y) =>
        (M11 * x + M12 * y + Dx, M21 * x + M22 * y + Dy);

    public (double X, double Y) ApplyVector(double x, double y) =>
        (M11 * x + M12 * y, M21 * x + M22 * y);

    /// <summary>等方的な平均スケール（文字サイズ等の近似用）。</summary>
    public double MeanScale =>
        (Math.Sqrt(M11 * M11 + M21 * M21) + Math.Sqrt(M12 * M12 + M22 * M22)) / 2;
}

/// <summary>図面座標(mm, Y上向き) → PDFページ座標(pt, Y下向き) の変換。</summary>
internal readonly struct RenderTransform
{
    public const double PtPerMm = 72.0 / 25.4;

    /// <summary>図面mm → ページpt の倍率。</summary>
    public readonly double Scale;
    /// <summary>コンテンツを90°回転して配置するか。</summary>
    public readonly bool Rotate90;
    public readonly double SrcCx, SrcCy;       // ソース矩形中心 (mm)
    public readonly double PageCx, PageCy;     // ページ中心 (pt)

    public RenderTransform(double scale, bool rotate90, double srcCx, double srcCy, double pageCx, double pageCy)
    {
        Scale = scale; Rotate90 = rotate90;
        SrcCx = srcCx; SrcCy = srcCy;
        PageCx = pageCx; PageCy = pageCy;
    }

    public (double X, double Y) Map(double x, double y)
    {
        double dx = x - SrcCx, dy = y - SrcCy;
        if (Rotate90) (dx, dy) = (-dy, dx);
        return (PageCx + dx * Scale, PageCy - dy * Scale);
    }

    /// <summary>方向ベクトルをページ座標系（Y下向き）へ。</summary>
    public (double X, double Y) MapVector(double x, double y)
    {
        if (Rotate90) (x, y) = (-y, x);
        return (x * Scale, -y * Scale);
    }
}

/// <summary>図面のバウンディングボックス計算（ブロック展開込み）。</summary>
internal static class BoundsCalculator
{
    public static (double MinX, double MinY, double MaxX, double MaxY)? Compute(JwwDocument doc)
    {
        var acc = new Accumulator();
        foreach (var e in doc.Entities)
            Add(ref acc, e, Affine2D.Identity, doc, 0);
        return acc.HasAny ? (acc.MinX, acc.MinY, acc.MaxX, acc.MaxY) : null;
    }

    private struct Accumulator
    {
        public double MinX = double.MaxValue, MinY = double.MaxValue;
        public double MaxX = double.MinValue, MaxY = double.MinValue;
        public bool HasAny = false;

        public Accumulator() { }

        public void Point(double x, double y)
        {
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) return;
            if (x < MinX) MinX = x;
            if (y < MinY) MinY = y;
            if (x > MaxX) MaxX = x;
            if (y > MaxY) MaxY = y;
            HasAny = true;
        }
    }

    private static void Add(ref Accumulator acc, JwwEntity e, in Affine2D m, JwwDocument doc, int depth)
    {
        switch (e)
        {
            case JwwLine l:
                AddPt(ref acc, m, l.X0, l.Y0);
                AddPt(ref acc, m, l.X1, l.Y1);
                break;
            case JwwArc a:
                double r = Math.Abs(a.Radius);
                AddPt(ref acc, m, a.CenterX - r, a.CenterY - r);
                AddPt(ref acc, m, a.CenterX + r, a.CenterY + r);
                break;
            case JwwPoint p:
                if (!p.IsTemporary) AddPt(ref acc, m, p.X, p.Y);
                break;
            case JwwText t:
                AddPt(ref acc, m, t.X0, t.Y0);
                AddPt(ref acc, m, t.X1, t.Y1 + t.SizeY);
                break;
            case JwwSolid s:
                if (s.PenStyle >= 101)
                {
                    // 円ソリッド: X1=半径
                    double sr = Math.Abs(s.X1);
                    AddPt(ref acc, m, s.X0 - sr, s.Y0 - sr);
                    AddPt(ref acc, m, s.X0 + sr, s.Y0 + sr);
                }
                else
                {
                    AddPt(ref acc, m, s.X0, s.Y0);
                    AddPt(ref acc, m, s.X1, s.Y1);
                    AddPt(ref acc, m, s.X2, s.Y2);
                    AddPt(ref acc, m, s.X3, s.Y3);
                }
                break;
            case JwwDimension d:
                Add(ref acc, d.Line, m, doc, depth);
                Add(ref acc, d.Text, m, doc, depth);
                if (d.AuxLine1 is not null) Add(ref acc, d.AuxLine1, m, doc, depth);
                if (d.AuxLine2 is not null) Add(ref acc, d.AuxLine2, m, doc, depth);
                break;
            case JwwBlockInsert b when depth < 32:
                if (doc.BlockDefinitions.TryGetValue(b.DefinitionNumber, out var def))
                {
                    var local = m.Compose(Affine2D.BlockInsert(b.BaseX, b.BaseY, b.ScaleX, b.ScaleY, b.Rotation));
                    foreach (var child in def.Entities)
                        Add(ref acc, child, local, doc, depth + 1);
                }
                break;
        }
    }

    private static void AddPt(ref Accumulator acc, in Affine2D m, double x, double y)
    {
        var (px, py) = m.Apply(x, y);
        acc.Point(px, py);
    }
}
