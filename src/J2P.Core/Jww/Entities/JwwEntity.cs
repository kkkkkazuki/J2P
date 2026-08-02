namespace J2P.Core.Jww.Entities;

/// <summary>全図形データ共通の属性。</summary>
public abstract class JwwEntity
{
    /// <summary>曲線属性番号。</summary>
    public uint Group { get; internal set; }

    /// <summary>線種番号（1〜9: 標準、11〜15: ランダム線、16〜19: 倍長線、31〜: SXF、100: 点マーカー）。</summary>
    public byte PenStyle { get; internal set; }

    /// <summary>線色番号（1〜9: 標準、10: 任意色ソリッド、101〜: SXF拡張線色+100）。</summary>
    public ushort PenColor { get; internal set; }

    /// <summary>個別線幅（1/100mm 単位、0なら線色既定、Ver.3.51以降）。</summary>
    public ushort PenWidth { get; internal set; }

    /// <summary>レイヤ番号（0〜15）。</summary>
    public ushort Layer { get; internal set; }

    /// <summary>レイヤグループ番号（0〜15）。</summary>
    public ushort GroupLayer { get; internal set; }

    /// <summary>属性フラグ。</summary>
    public ushort Flags { get; internal set; }

    internal void ReadCommon(CArchiveReader ar, uint version)
    {
        Group = ar.UInt32();
        PenStyle = ar.Byte();
        PenColor = ar.UInt16();
        PenWidth = version >= 351 ? ar.UInt16() : (ushort)0;
        Layer = ar.UInt16();
        GroupLayer = ar.UInt16();
        Flags = ar.UInt16();
    }

    internal void CopyCommonFrom(JwwEntity src)
    {
        Group = src.Group;
        PenStyle = src.PenStyle;
        PenColor = src.PenColor;
        PenWidth = src.PenWidth;
        Layer = src.Layer;
        GroupLayer = src.GroupLayer;
        Flags = src.Flags;
    }
}

/// <summary>線。</summary>
public sealed class JwwLine : JwwEntity
{
    public double X0 { get; internal set; }
    public double Y0 { get; internal set; }
    public double X1 { get; internal set; }
    public double Y1 { get; internal set; }

    internal static JwwLine Read(CArchiveReader ar, uint version)
    {
        var e = new JwwLine();
        e.ReadCommon(ar, version);
        e.X0 = ar.Double(); e.Y0 = ar.Double();
        e.X1 = ar.Double(); e.Y1 = ar.Double();
        return e;
    }
}

/// <summary>円・円弧・楕円（弧）。</summary>
public sealed class JwwArc : JwwEntity
{
    /// <summary>中心X（mm）。</summary>
    public double CenterX { get; internal set; }
    /// <summary>中心Y（mm）。</summary>
    public double CenterY { get; internal set; }
    /// <summary>半径（楕円のときは長軸方向の半径、mm）。</summary>
    public double Radius { get; internal set; }
    /// <summary>開始角（ラジアン、傾き前の楕円座標系）。</summary>
    public double StartAngle { get; internal set; }
    /// <summary>円弧角＝開始角からの角度スパン（ラジアン）。</summary>
    public double SweepAngle { get; internal set; }
    /// <summary>傾き角（ラジアン）。</summary>
    public double TiltAngle { get; internal set; }
    /// <summary>扁平率（短径/長径。1.0で真円）。</summary>
    public double Flatness { get; internal set; }
    /// <summary>全円フラグ。</summary>
    public bool IsFullCircle { get; internal set; }

    internal static JwwArc Read(CArchiveReader ar, uint version)
    {
        var e = new JwwArc();
        e.ReadCommon(ar, version);
        e.CenterX = ar.Double();
        e.CenterY = ar.Double();
        e.Radius = ar.Double();
        e.StartAngle = ar.Double();
        e.SweepAngle = ar.Double();
        e.TiltAngle = ar.Double();
        e.Flatness = ar.Double();
        e.IsFullCircle = ar.UInt32() != 0;
        return e;
    }
}

/// <summary>点（実点・仮点・点マーカー）。</summary>
public sealed class JwwPoint : JwwEntity
{
    public double X { get; internal set; }
    public double Y { get; internal set; }
    /// <summary>仮点フラグ（仮点は印刷されない）。</summary>
    public bool IsTemporary { get; internal set; }
    /// <summary>点マーカーコード（線種番号100のときのみ有効）。</summary>
    public uint MarkerCode { get; internal set; }
    /// <summary>点マーカーの回転角（ラジアン）。</summary>
    public double MarkerRotation { get; internal set; }
    /// <summary>点マーカーの倍率。</summary>
    public double MarkerScale { get; internal set; }

    internal static JwwPoint Read(CArchiveReader ar, uint version)
    {
        var e = new JwwPoint();
        e.ReadCommon(ar, version);
        e.X = ar.Double();
        e.Y = ar.Double();
        e.IsTemporary = ar.UInt32() != 0;
        if (e.PenStyle == 100)
        {
            e.MarkerCode = ar.UInt32();
            e.MarkerRotation = ar.Double();
            e.MarkerScale = ar.Double();
        }
        return e;
    }
}

/// <summary>文字。</summary>
public sealed class JwwText : JwwEntity
{
    /// <summary>文字列基点X（mm）。</summary>
    public double X0 { get; internal set; }
    /// <summary>文字列基点Y（mm）。</summary>
    public double Y0 { get; internal set; }
    /// <summary>文字列終点X（mm）。</summary>
    public double X1 { get; internal set; }
    /// <summary>文字列終点Y（mm）。</summary>
    public double Y1 { get; internal set; }
    /// <summary>文字種（1〜10: 定義済み文字種、0: 任意サイズ）。</summary>
    public uint TextType { get; internal set; }
    /// <summary>文字幅（mm）。</summary>
    public double SizeX { get; internal set; }
    /// <summary>文字高（mm）。</summary>
    public double SizeY { get; internal set; }
    /// <summary>字間（mm）。</summary>
    public double Spacing { get; internal set; }
    /// <summary>角度（度）。</summary>
    public double AngleDeg { get; internal set; }
    /// <summary>フォント名（例: ＭＳ ゴシック）。</summary>
    public string FontName { get; internal set; } = string.Empty;
    /// <summary>文字列本体（"^@BM…" は画像参照などの特殊文字列）。</summary>
    public string Text { get; internal set; } = string.Empty;

    internal static JwwText Read(CArchiveReader ar, uint version)
    {
        var e = new JwwText();
        e.ReadCommon(ar, version);
        e.X0 = ar.Double(); e.Y0 = ar.Double();
        e.X1 = ar.Double(); e.Y1 = ar.Double();
        e.TextType = ar.UInt32();
        e.SizeX = ar.Double();
        e.SizeY = ar.Double();
        e.Spacing = ar.Double();
        e.AngleDeg = ar.Double();
        e.FontName = ar.String();
        e.Text = ar.String();
        return e;
    }
}

/// <summary>ソリッド（塗りつぶし四角形。三角形は2点が一致）。</summary>
public sealed class JwwSolid : JwwEntity
{
    public double X0 { get; internal set; }
    public double Y0 { get; internal set; }
    public double X1 { get; internal set; }
    public double Y1 { get; internal set; }
    public double X2 { get; internal set; }
    public double Y2 { get; internal set; }
    public double X3 { get; internal set; }
    public double Y3 { get; internal set; }
    /// <summary>任意色（COLORREF: 0x00BBGGRR）。線色番号が10のときのみ有効。</summary>
    public uint? Rgb { get; internal set; }

    internal static JwwSolid Read(CArchiveReader ar, uint version)
    {
        var e = new JwwSolid();
        e.ReadCommon(ar, version);
        e.X0 = ar.Double(); e.Y0 = ar.Double();
        e.X1 = ar.Double(); e.Y1 = ar.Double();
        e.X2 = ar.Double(); e.Y2 = ar.Double();
        e.X3 = ar.Double(); e.Y3 = ar.Double();
        if (e.PenColor == 10)
            e.Rgb = ar.UInt32();
        return e;
    }
}

/// <summary>寸法図形（寸法線＋寸法値＋補助要素の複合）。</summary>
public sealed class JwwDimension : JwwEntity
{
    public JwwLine Line { get; internal set; } = new();
    public JwwText Text { get; internal set; } = new();
    public bool SxfMode { get; internal set; }
    /// <summary>引出線など補助線（Ver.4.20以降）。</summary>
    public JwwLine? AuxLine1 { get; internal set; }
    public JwwLine? AuxLine2 { get; internal set; }
    public JwwPoint? EndPoint1 { get; internal set; }
    public JwwPoint? EndPoint2 { get; internal set; }
    public JwwPoint? AuxPoint1 { get; internal set; }
    public JwwPoint? AuxPoint2 { get; internal set; }

    internal static JwwDimension Read(CArchiveReader ar, uint version)
    {
        var e = new JwwDimension();
        e.ReadCommon(ar, version);
        e.Line = JwwLine.Read(ar, version);
        e.Text = JwwText.Read(ar, version);
        if (version >= 420)
        {
            e.SxfMode = ar.UInt16() != 0;
            e.AuxLine1 = JwwLine.Read(ar, version);
            e.AuxLine2 = JwwLine.Read(ar, version);
            e.EndPoint1 = JwwPoint.Read(ar, version);
            e.EndPoint2 = JwwPoint.Read(ar, version);
            e.AuxPoint1 = JwwPoint.Read(ar, version);
            e.AuxPoint2 = JwwPoint.Read(ar, version);
        }
        return e;
    }
}

/// <summary>ブロック参照（配置）。</summary>
public sealed class JwwBlockInsert : JwwEntity
{
    /// <summary>配置基準点X（mm）。</summary>
    public double BaseX { get; internal set; }
    /// <summary>配置基準点Y（mm）。</summary>
    public double BaseY { get; internal set; }
    public double ScaleX { get; internal set; } = 1.0;
    public double ScaleY { get; internal set; } = 1.0;
    /// <summary>回転角（ラジアン）。</summary>
    public double Rotation { get; internal set; }
    /// <summary>参照するブロック定義の通し番号。</summary>
    public uint DefinitionNumber { get; internal set; }

    internal static JwwBlockInsert Read(CArchiveReader ar, uint version)
    {
        var e = new JwwBlockInsert();
        e.ReadCommon(ar, version);
        e.BaseX = ar.Double();
        e.BaseY = ar.Double();
        e.ScaleX = ar.Double();
        e.ScaleY = ar.Double();
        e.Rotation = ar.Double();
        e.DefinitionNumber = ar.UInt32();
        return e;
    }
}

/// <summary>ブロック定義（図形データの実体リスト）。</summary>
public sealed class JwwBlockDefinition : JwwEntity
{
    /// <summary>定義の通し番号（JwwBlockInsert.DefinitionNumber が参照）。</summary>
    public int Number { get; internal set; }
    /// <summary>参照されているか。</summary>
    public bool IsReferenced { get; internal set; }
    /// <summary>定義名（Ver.4.10以降 "@@SfigorgFlag@@" 付加あり）。</summary>
    public string Name { get; internal set; } = string.Empty;
    /// <summary>定義に含まれる図形。</summary>
    public IReadOnlyList<JwwEntity> Entities => EntitiesInternal;

    internal List<JwwEntity> EntitiesInternal { get; } = new();
}
