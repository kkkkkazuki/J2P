namespace J2P.Core.Pdf;

/// <summary>印刷範囲（ソース矩形）の決め方。</summary>
public enum PrintAreaMode
{
    /// <summary>ファイルに保存されたJw_cadの印刷設定（出力範囲原点・倍率・90°回転）に従う。
    /// 設定が無効な場合は用紙全体へ自動フォールバック。</summary>
    FilePrintSettings,
    /// <summary>図面用紙全体（用紙中心合わせ）。</summary>
    PaperFull,
    /// <summary>図形全体のバウンディングボックスにフィット。</summary>
    FitDrawing,
}

/// <summary>出力用紙サイズ。</summary>
public enum PaperSelection
{
    /// <summary>元図面と同じ用紙サイズ。</summary>
    Auto,
    A4,
    A3,
    A2,
    A1,
    A0,
}

/// <summary>用紙方向。</summary>
public enum PaperOrientation
{
    /// <summary>自動（元図面の向き。Jw_cadの90°回転出力指定があれば縦）。</summary>
    Auto,
    /// <summary>縦。</summary>
    Portrait,
    /// <summary>横。</summary>
    Landscape,
}

/// <summary>色モード。</summary>
public enum ColorMode
{
    Color,
    BlackAndWhite,
}

/// <summary>PDF描画オプション。</summary>
public sealed class PdfRenderOptions
{
    public PrintAreaMode PrintArea { get; set; } = PrintAreaMode.FilePrintSettings;

    public PaperSelection Paper { get; set; } = PaperSelection.Auto;

    public PaperOrientation Orientation { get; set; } = PaperOrientation.Auto;

    /// <summary>印刷倍率。null で自動（ファイルの印刷倍率／用紙フィット）。1.0 = 100%。</summary>
    public double? Magnification { get; set; }

    public ColorMode Color { get; set; } = ColorMode.Color;

    /// <summary>出力用紙の寸法（mm、横長）。</summary>
    public (double Width, double Height) GetPaperSizeMm(uint sourcePaperCode) => Paper switch
    {
        PaperSelection.A4 => (297, 210),
        PaperSelection.A3 => (420, 297),
        PaperSelection.A2 => (594, 420),
        PaperSelection.A1 => (841, 594),
        PaperSelection.A0 => (1189, 841),
        _ => Jww.JwwPaperSizes.GetSizeMm(sourcePaperCode),
    };
}
