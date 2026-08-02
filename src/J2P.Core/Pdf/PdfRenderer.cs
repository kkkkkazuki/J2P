using J2P.Core.Jww;
using J2P.Core.Jww.Entities;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace J2P.Core.Pdf;

/// <summary>JwwDocument を PDF の1ページとして描画する。</summary>
public static class PdfRenderer
{
    private const int MaxBlockDepth = 32;

    /// <summary>doc を pdf に1ページ追加して描画し、警告一覧を返す。</summary>
    public static IReadOnlyList<string> Render(JwwDocument doc, PdfRenderOptions options, PdfDocument pdf)
    {
        JapaneseFontResolver.Install();

        var warnings = new List<string>(doc.Warnings);
        var layout = ComputeLayout(doc, options, warnings);

        var page = pdf.AddPage();
        page.Width = XUnit.FromPoint(layout.PageWidthPt);
        page.Height = XUnit.FromPoint(layout.PageHeightPt);

        using var gfx = XGraphics.FromPdfPage(page);
        var ctx = new RenderContext(gfx, layout.Transform, doc, options, warnings);

        if (doc.Header.SolidsFirst)
        {
            foreach (var e in doc.Entities)
                if (e is JwwSolid) ctx.Draw(e, Affine2D.Identity, 0);
            foreach (var e in doc.Entities)
                if (e is not JwwSolid) ctx.Draw(e, Affine2D.Identity, 0);
        }
        else
        {
            foreach (var e in doc.Entities)
                ctx.Draw(e, Affine2D.Identity, 0);
        }

        ctx.FlushWarnings();
        return warnings;
    }

    /// <summary>単一ファイルをストリームへ変換する補助。</summary>
    public static IReadOnlyList<string> RenderToStream(JwwDocument doc, PdfRenderOptions options, Stream output)
    {
        using var pdf = new PdfDocument();
        var warnings = Render(doc, options, pdf);
        pdf.Save(output);
        return warnings;
    }

    // ---- レイアウト決定 ----

    internal readonly record struct Layout(double PageWidthPt, double PageHeightPt, RenderTransform Transform);

    internal static Layout ComputeLayout(JwwDocument doc, PdfRenderOptions options, List<string> warnings)
    {
        var h = doc.Header;
        var (srcW, srcH) = JwwPaperSizes.GetSizeMm(h.PaperSizeCode);
        var (paperW, paperH) = options.GetPaperSizeMm(h.PaperSizeCode);

        var mode = options.PrintArea;

        // ファイルの印刷設定の有効性チェック
        double bair = h.PrintScale;
        if (mode == PrintAreaMode.FilePrintSettings && (double.IsNaN(bair) || bair <= 0.0001 || bair >= 10000))
        {
            warnings.Add("ファイルの印刷倍率が無効なため、用紙全体を出力します。");
            mode = PrintAreaMode.PaperFull;
        }

        (double MinX, double MinY, double MaxX, double MaxY)? bounds = null;
        if (mode == PrintAreaMode.FitDrawing)
        {
            bounds = BoundsCalculator.Compute(doc);
            if (bounds is null)
            {
                warnings.Add("図形が無いため、用紙全体を出力します。");
                mode = PrintAreaMode.PaperFull;
            }
        }

        bool rotate;
        double srcRectW, srcRectH, srcCx, srcCy, scale;
        double pageWmm, pageHmm;

        if (mode == PrintAreaMode.FilePrintSettings)
        {
            rotate = h.PrintRotated;
            // ページ向き: 自動なら 90°回転出力時は縦
            bool portrait = options.Orientation switch
            {
                PaperOrientation.Portrait => true,
                PaperOrientation.Landscape => false,
                _ => rotate,
            };
            (pageWmm, pageHmm) = portrait ? (paperH, paperW) : (paperW, paperH);
            // 向きを明示指定された場合は回転をページ向きに合わせ直す
            rotate = pageWmm < pageHmm;

            double mag = options.Magnification ?? bair;
            // 印刷枠（図面座標）: ページを回転の逆向きで図面空間に置き、倍率で割る
            srcRectW = (rotate ? pageHmm : pageWmm) / mag;
            srcRectH = (rotate ? pageWmm : pageHmm) / mag;
            // m_DPPrtGenten は印刷枠の「基準点」。枠内のどこを指すかは基準点位置コードで決まり、
            // 0(無指定)なら枠の中心。ここから枠中心へのオフセットを足す。
            srcCx = h.PrintOriginX + BaseOffsetX(h.PrintBasePosition, srcRectW);
            srcCy = h.PrintOriginY + BaseOffsetY(h.PrintBasePosition, srcRectH);
            scale = RenderTransform.PtPerMm * mag;
        }
        else
        {
            if (mode == PrintAreaMode.FitDrawing && bounds is { } bb)
            {
                const double margin = 5.0;
                srcRectW = Math.Max(bb.MaxX - bb.MinX, 1.0) + margin * 2;
                srcRectH = Math.Max(bb.MaxY - bb.MinY, 1.0) + margin * 2;
                srcCx = (bb.MinX + bb.MaxX) / 2;
                srcCy = (bb.MinY + bb.MaxY) / 2;
            }
            else
            {
                srcRectW = srcW;
                srcRectH = srcH;
                srcCx = 0;
                srcCy = 0;
            }

            bool portrait = options.Orientation switch
            {
                PaperOrientation.Portrait => true,
                PaperOrientation.Landscape => false,
                _ => srcRectW < srcRectH,
            };
            (pageWmm, pageHmm) = portrait ? (paperH, paperW) : (paperW, paperH);
            rotate = (pageWmm >= pageHmm) != (srcRectW >= srcRectH);

            double effW = rotate ? pageHmm : pageWmm;
            double effH = rotate ? pageWmm : pageHmm;
            scale = options.Magnification is { } m
                ? RenderTransform.PtPerMm * m
                : RenderTransform.PtPerMm * Math.Min(effW / srcRectW, effH / srcRectH);
        }

        double pageWpt = pageWmm * RenderTransform.PtPerMm;
        double pageHpt = pageHmm * RenderTransform.PtPerMm;
        var transform = new RenderTransform(scale, rotate, srcCx, srcCy, pageWpt / 2, pageHpt / 2);
        return new Layout(pageWpt, pageHpt, transform);
    }

    /// <summary>
    /// プリンタ出力基準点位置（テンキー配置。1=左下 … 9=右上、0=無指定）から、
    /// 基準点 → 印刷枠中心 のXオフセットを返す。
    /// </summary>
    private static double BaseOffsetX(uint basePosition, double frameWidth) => basePosition switch
    {
        1 or 4 or 7 => frameWidth / 2,    // 左端が基準 → 中心は右へ
        3 or 6 or 9 => -frameWidth / 2,   // 右端が基準
        _ => 0,                           // 0(無指定)・2・5・8 は横方向中央
    };

    /// <summary>基準点 → 印刷枠中心 のYオフセット（図面座標はY上向き）。</summary>
    private static double BaseOffsetY(uint basePosition, double frameHeight) => basePosition switch
    {
        1 or 2 or 3 => frameHeight / 2,   // 下端が基準 → 中心は上へ
        7 or 8 or 9 => -frameHeight / 2,  // 上端が基準
        _ => 0,                           // 0(無指定)・4・5・6 は縦方向中央
    };

    // ---- 描画コンテキスト ----

    private sealed class RenderContext
    {
        private readonly XGraphics _gfx;
        private readonly RenderTransform _t;
        private readonly JwwDocument _doc;
        private readonly PdfRenderOptions _options;
        private readonly List<string> _warnings;
        private readonly JwwHeader _h;
        private int _skippedImages;
        private int _missingBlocks;

        public RenderContext(XGraphics gfx, RenderTransform t, JwwDocument doc,
            PdfRenderOptions options, List<string> warnings)
        {
            _gfx = gfx;
            _t = t;
            _doc = doc;
            _options = options;
            _warnings = warnings;
            _h = doc.Header;
        }

        public void FlushWarnings()
        {
            if (_skippedImages > 0)
                _warnings.Add($"画像参照の文字 {_skippedImages} 件は描画されません（未対応）。");
            if (_missingBlocks > 0)
                _warnings.Add($"未定義のブロック参照 {_missingBlocks} 件をスキップしました。");
        }

        public void Draw(JwwEntity e, in Affine2D m, int depth)
        {
            // 非表示レイヤは印刷されない
            if (depth == 0 && !_h.IsLayerVisible(e.GroupLayer, e.Layer)) return;

            switch (e)
            {
                case JwwLine l: DrawLine(l, m); break;
                case JwwArc a: DrawArc(a, m); break;
                case JwwPoint p: DrawPoint(p, m); break;
                case JwwText t: DrawText(t, m); break;
                case JwwSolid s: DrawSolid(s, m); break;
                case JwwDimension d: DrawDimension(d, m, depth); break;
                case JwwBlockInsert b: DrawBlock(b, m, depth); break;
            }
        }

        private void DrawLine(JwwLine l, in Affine2D m)
        {
            if (l.PenStyle == 9) return; // 補助線種は印刷されない
            var pen = MakePen(l);
            var p0 = MapPoint(m, l.X0, l.Y0);
            var p1 = MapPoint(m, l.X1, l.Y1);
            _gfx.DrawLine(pen, p0, p1);
        }

        private void DrawArc(JwwArc a, in Affine2D m)
        {
            if (a.PenStyle == 9) return;
            if (a.Radius == 0) return;
            var pen = MakePen(a);
            var path = new XGraphicsPath();
            double flat = a.Flatness == 0 ? 1.0 : a.Flatness;
            if (a.IsFullCircle)
            {
                AddEllipticalArc(path, m, a.CenterX, a.CenterY, a.Radius, flat, a.TiltAngle, 0, Math.PI * 2);
                path.CloseFigure();
            }
            else
            {
                AddEllipticalArc(path, m, a.CenterX, a.CenterY, a.Radius, flat, a.TiltAngle,
                    a.StartAngle, a.SweepAngle);
            }
            _gfx.DrawPath(pen, path);
        }

        private void DrawPoint(JwwPoint p, in Affine2D m)
        {
            if (p.IsTemporary) return; // 仮点は印刷されない
            var brush = new XSolidBrush(ResolveColor(p.PenColor, null));
            double rMm = _h.DrawPointsWithPrinterRadius && p.PenColor < 10
                ? Math.Max(_h.PrinterPointRadii[p.PenColor], 0.05)
                : Math.Max(ResolveWidthMm(p) * 1.5, 0.15);
            double rPt = rMm * RenderTransform.PtPerMm;
            var c = MapPoint(m, p.X, p.Y);
            _gfx.DrawEllipse(brush, c.X - rPt, c.Y - rPt, rPt * 2, rPt * 2);
        }

        private void DrawText(JwwText t, in Affine2D m)
        {
            if (string.IsNullOrEmpty(t.Text)) return;
            if (t.Text.StartsWith("^@", StringComparison.Ordinal))
            {
                // "^@BM…" は画像参照などの特殊文字列
                _skippedImages++;
                return;
            }

            double sizeMm = t.SizeY > 0 ? t.SizeY : 3.0;
            double fontSizePt = sizeMm * _t.Scale * m.MeanScale;
            if (fontSizePt < 0.5) return;

            string family = t.FontName.Contains("明朝") ? JapaneseFontResolver.Mincho : JapaneseFontResolver.Gothic;
            var font = new XFont(family, fontSizePt, XFontStyleEx.Regular);
            var brush = new XSolidBrush(ResolveColor(t.PenColor, null));

            double angleRad = t.AngleDeg * Math.PI / 180.0;
            var (dirX, dirY) = m.ApplyVector(Math.Cos(angleRad), Math.Sin(angleRad));
            var (pvx, pvy) = _t.MapVector(dirX, dirY);
            double pageAngleDeg = Math.Atan2(pvy, pvx) * 180.0 / Math.PI;

            var origin = MapPoint(m, t.X0, t.Y0);

            var state = _gfx.Save();
            try
            {
                _gfx.RotateAtTransform(pageAngleDeg, origin);
                if (t.Spacing != 0 || t.SizeX != t.SizeY)
                {
                    // 字間・幅指定があるときは1文字ずつ配置（全角=SizeX、半角=SizeX/2）
                    double advanceMm = 0;
                    foreach (var ch in t.Text)
                    {
                        double w = IsHalfWidth(ch) ? t.SizeX / 2 : t.SizeX;
                        var pos = new XPoint(origin.X + advanceMm * _t.Scale * m.MeanScale, origin.Y);
                        _gfx.DrawString(ch.ToString(), font, brush, pos, XStringFormats.BaseLineLeft);
                        advanceMm += w + t.Spacing;
                    }
                }
                else
                {
                    _gfx.DrawString(t.Text, font, brush, origin, XStringFormats.BaseLineLeft);
                }
            }
            finally
            {
                _gfx.Restore(state);
            }
        }

        private static bool IsHalfWidth(char c) =>
            c < 0x0080 || (c >= 0xFF61 && c <= 0xFF9F);

        private void DrawSolid(JwwSolid s, in Affine2D m)
        {
            var color = s.Rgb is { } rgb ? FromColorRef(rgb) : ResolveColor(s.PenColor, null);
            if (_options.Color == ColorMode.BlackAndWhite) color = XColors.Black;
            var brush = new XSolidBrush(color);

            if (s.PenStyle >= 101)
            {
                DrawCircleSolid(s, m, brush);
                return;
            }

            // 四角形ソリッド（頂点は 0-1-3-2 の順で外周をなす）
            var pts = new[]
            {
                MapPoint(m, s.X0, s.Y0),
                MapPoint(m, s.X1, s.Y1),
                MapPoint(m, s.X3, s.Y3),
                MapPoint(m, s.X2, s.Y2),
            };
            _gfx.DrawPolygon(brush, pts, XFillMode.Winding);
        }

        private void DrawCircleSolid(JwwSolid s, in Affine2D m, XSolidBrush brush)
        {
            // 円ソリッド: X0,Y0=中心、X1=半径、Y1=扁平率、X2=傾き角、Y2=開始角、X3=円弧角、Y3=種別
            double cx = s.X0, cy = s.Y0;
            double r = Math.Abs(s.X1);
            if (r == 0) return;
            double flat = s.Y1 == 0 ? 1.0 : s.Y1;
            double tilt = s.X2;
            double start = s.Y2;
            double sweep = s.X3;
            var path = new XGraphicsPath();

            if (s.PenStyle >= 105)
            {
                // 円環ソリッド: Y3=内側の円半径
                double inner = Math.Abs(s.Y3);
                bool fullRing = Math.Abs(sweep) >= Math.PI * 2 - 1e-9 || sweep == 0;
                if (fullRing)
                {
                    path.FillMode = XFillMode.Alternate;
                    AddEllipticalArc(path, m, cx, cy, r, flat, tilt, 0, Math.PI * 2);
                    path.CloseFigure();
                    if (inner > 0)
                    {
                        path.StartFigure();
                        AddEllipticalArc(path, m, cx, cy, inner, flat, tilt, 0, Math.PI * 2);
                        path.CloseFigure();
                    }
                }
                else
                {
                    // 円環の一部: 外弧 → 内弧（逆向き）で閉じる
                    AddEllipticalArc(path, m, cx, cy, r, flat, tilt, start, sweep);
                    AddEllipticalArc(path, m, cx, cy, inner, flat, tilt, start + sweep, -sweep);
                    path.CloseFigure();
                }
            }
            else if (s.Y3 >= 100)
            {
                // 全円ソリッド
                AddEllipticalArc(path, m, cx, cy, r, flat, tilt, 0, Math.PI * 2);
                path.CloseFigure();
            }
            else if (s.Y3 == 0)
            {
                // 扇形: 中心 → 弧 → 中心
                var center = MapPoint(m, cx, cy);
                var startPt = EllipsePoint(m, cx, cy, r, flat, tilt, start);
                path.AddLine(center, startPt);
                AddEllipticalArc(path, m, cx, cy, r, flat, tilt, start, sweep);
                path.CloseFigure();
            }
            else
            {
                // 弓形（および外側円弧ソリッドの近似）: 弧＋弦で閉じる
                AddEllipticalArc(path, m, cx, cy, r, flat, tilt, start, sweep);
                path.CloseFigure();
            }

            _gfx.DrawPath(brush, path);
        }

        private void DrawDimension(JwwDimension d, in Affine2D m, int depth)
        {
            DrawLine(d.Line, m);
            DrawText(d.Text, m);
            if (d.AuxLine1 is not null) DrawLine(d.AuxLine1, m);
            if (d.AuxLine2 is not null) DrawLine(d.AuxLine2, m);
            if (d.EndPoint1 is not null) DrawPoint(d.EndPoint1, m);
            if (d.EndPoint2 is not null) DrawPoint(d.EndPoint2, m);
        }

        private void DrawBlock(JwwBlockInsert b, in Affine2D m, int depth)
        {
            if (depth >= MaxBlockDepth) return;
            if (!_doc.BlockDefinitions.TryGetValue(b.DefinitionNumber, out var def))
            {
                _missingBlocks++;
                return;
            }
            var local = m.Compose(Affine2D.BlockInsert(b.BaseX, b.BaseY, b.ScaleX, b.ScaleY, b.Rotation));
            foreach (var child in def.Entities)
                Draw(child, local, depth + 1);
        }

        // ---- 幾何ヘルパ ----

        private XPoint MapPoint(in Affine2D m, double x, double y)
        {
            var (ax, ay) = m.Apply(x, y);
            var (px, py) = _t.Map(ax, ay);
            return new XPoint(px, py);
        }

        private XPoint EllipsePoint(in Affine2D m, double cx, double cy, double r, double flat, double tilt, double t)
        {
            double ux = Math.Cos(tilt), uy = Math.Sin(tilt);
            double x = cx + r * Math.Cos(t) * ux - r * flat * Math.Sin(t) * uy;
            double y = cy + r * Math.Cos(t) * uy + r * flat * Math.Sin(t) * ux;
            return MapPoint(m, x, y);
        }

        /// <summary>楕円弧をベジェ近似でパスに追加する（図面座標→ページ座標へ写像済みの点で構成）。</summary>
        private void AddEllipticalArc(XGraphicsPath path, in Affine2D m,
            double cx, double cy, double r, double flat, double tilt, double t0, double sweep)
        {
            if (sweep == 0) return;
            int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2)));
            double step = sweep / segments;

            double ux = Math.Cos(tilt), uy = Math.Sin(tilt);

            (double X, double Y) P(double t) => (
                cx + r * Math.Cos(t) * ux - r * flat * Math.Sin(t) * uy,
                cy + r * Math.Cos(t) * uy + r * flat * Math.Sin(t) * ux);

            (double X, double Y) D(double t) => (
                -r * Math.Sin(t) * ux - r * flat * Math.Cos(t) * uy,
                -r * Math.Sin(t) * uy + r * flat * Math.Cos(t) * ux);

            for (int i = 0; i < segments; i++)
            {
                double a = t0 + step * i;
                double b = a + step;
                double k = 4.0 / 3.0 * Math.Tan((b - a) / 4.0);
                var p0 = P(a);
                var p3 = P(b);
                var d0 = D(a);
                var d3 = D(b);
                var c1 = (X: p0.X + k * d0.X, Y: p0.Y + k * d0.Y);
                var c2 = (X: p3.X - k * d3.X, Y: p3.Y - k * d3.Y);
                path.AddBezier(
                    MapPoint(m, p0.X, p0.Y),
                    MapPoint(m, c1.X, c1.Y),
                    MapPoint(m, c2.X, c2.Y),
                    MapPoint(m, p3.X, p3.Y));
            }
        }

        // ---- ペン・色の解決 ----

        private XPen MakePen(JwwEntity e)
        {
            double widthMm = ResolveWidthMm(e);
            double widthPt = Math.Max(widthMm * RenderTransform.PtPerMm, 0.1);
            var pen = new XPen(ResolveColor(e.PenColor, null), widthPt)
            {
                LineCap = XLineCap.Round,
                LineJoin = XLineJoin.Round,
            };

            var dashMm = ResolveDashPatternMm(e.PenStyle);
            if (dashMm is not null)
            {
                // XPen.DashPattern は線幅の倍数で指定する
                var pattern = new double[dashMm.Length];
                for (int i = 0; i < dashMm.Length; i++)
                    pattern[i] = Math.Max(dashMm[i] * RenderTransform.PtPerMm / widthPt, 0.1);
                pen.DashPattern = pattern;
            }
            return pen;
        }

        private double ResolveWidthMm(JwwEntity e)
        {
            // 個別線幅（1/100mm）が優先
            if (e.PenWidth > 0)
                return e.PenWidth / 100.0;

            uint table;
            if (e.PenColor >= 100)
            {
                int idx = e.PenColor - 100;
                table = idx < _h.SxfPrinterPenWidths.Length ? _h.SxfPrinterPenWidths[idx] : 0;
                if (table > 0) return table / 100.0; // SXF幅は常に1/100mm
                return 0.15;
            }

            int c = Math.Min((int)e.PenColor, 9);
            table = _h.PrinterPenWidths[c];
            if (table == 0) return 0.15;

            return _h.WidthsAreHundredthsMm
                ? table / 100.0
                : table * 25.4 / _h.EffectiveDpi; // 旧形式はプリンタドット数
        }

        private XColor ResolveColor(ushort penColor, uint? rgb)
        {
            if (_options.Color == ColorMode.BlackAndWhite)
                return XColors.Black;
            if (rgb is { } v)
                return FromColorRef(v);
            if (penColor >= 100)
            {
                int idx = penColor - 100;
                if (idx < _h.SxfPrinterPenColors.Length)
                    return FromColorRef(_h.SxfPrinterPenColors[idx]);
                return XColors.Black;
            }
            int c = Math.Min((int)penColor, 9);
            return FromColorRef(_h.PrinterPenColors[c]);
        }

        private static XColor FromColorRef(uint colorRef) => XColor.FromArgb(
            (byte)(colorRef & 0xFF),
            (byte)((colorRef >> 8) & 0xFF),
            (byte)((colorRef >> 16) & 0xFF));

        /// <summary>線種番号 → 破線パターン（mm）。実線・未対応は null。</summary>
        private double[]? ResolveDashPatternMm(byte penStyle)
        {
            switch (penStyle)
            {
                case <= 1:
                    return null;
                case >= 2 and <= 8:
                {
                    int idx = penStyle - 2;
                    var decoded = JwwLineTypes.Decode(
                        _h.LineTypePatterns[idx], _h.LineTypeUnitDots[idx], _h.LineTypePrinterPitches[idx]);
                    return decoded ?? FallbackDash(penStyle);
                }
                case >= 11 and <= 19:
                    // ランダム線・倍長線種は実線で近似
                    return null;
                case >= 31 and <= 62:
                {
                    int idx = penStyle - 30;
                    if (idx < _h.SxfLineTypeSegments.Length)
                    {
                        uint seg = _h.SxfLineTypeSegments[idx];
                        if (seg > 0)
                        {
                            var list = new List<double>();
                            int n = (int)Math.Min(seg * 2, 10);
                            for (int i = 0; i < n; i++)
                            {
                                double p = _h.SxfLineTypePitches[idx, i];
                                if (p <= 0) break;
                                list.Add(p);
                            }
                            if (list.Count >= 2) return list.ToArray();
                        }
                    }
                    return null;
                }
                default:
                    return null;
            }
        }

        private static double[] FallbackDash(byte penStyle) => penStyle switch
        {
            2 => new[] { 0.6, 0.6 },            // 点線1
            3 => new[] { 1.2, 1.2 },            // 点線2
            4 => new[] { 2.4, 1.2 },            // 点線3
            5 => new[] { 4.0, 1.0, 0.6, 1.0 },  // 一点鎖1
            6 => new[] { 8.0, 2.0, 1.2, 2.0 },  // 一点鎖2
            7 => new[] { 4.0, 1.0, 0.6, 1.0, 0.6, 1.0 }, // 二点鎖1
            8 => new[] { 8.0, 2.0, 1.2, 2.0, 1.2, 2.0 }, // 二点鎖2
            _ => new[] { 2.0, 1.0 },
        };
    }
}
