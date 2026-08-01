namespace J2P.Core.Jww;

/// <summary>
/// JWWファイルのヘッダ情報。
/// フィールドの並びは Jw_cad 付属フォーマット文書（jwdatafmt.txt）のとおり全て読み進めるが、
/// 本ツールで使わない値は保持しない。
/// </summary>
public sealed class JwwHeader
{
    /// <summary>内部データバージョン（例: Jw_cad 7.02 → 700）。</summary>
    public uint Version { get; internal set; }

    /// <summary>ファイルメモ。</summary>
    public string Memo { get; internal set; } = string.Empty;

    /// <summary>
    /// 図面（用紙）サイズコード。0〜4: A0〜A4、8: 2A、9: 3A、10: 4A、11: 5A、
    /// 12: 10m、13: 50m、14: 100m。
    /// </summary>
    public uint PaperSizeCode { get; internal set; }

    /// <summary>書込レイヤグループ番号（0〜15）。</summary>
    public uint WriteGroup { get; internal set; }

    /// <summary>レイヤグループ状態（0:非表示、1:表示のみ、2:編集可能、3:書込）。</summary>
    public uint[] GroupStates { get; } = new uint[16];

    /// <summary>レイヤグループごとの縮尺の分母（1/100 なら 100.0）。</summary>
    public double[] GroupScales { get; } = new double[16];

    /// <summary>レイヤ状態 [グループ, レイヤ]（0:非表示、1:表示のみ、2:編集可能、3:書込）。</summary>
    public uint[,] LayerStates { get; } = new uint[16, 16];

    /// <summary>レイヤ名 [グループ, レイヤ]。</summary>
    public string[,] LayerNames { get; } = new string[16, 16];

    /// <summary>レイヤグループ名。</summary>
    public string[] GroupNames { get; } = new string[16];

    /// <summary>プリンタ出力範囲の原点X（図面座標mm）。</summary>
    public double PrintOriginX { get; internal set; }

    /// <summary>プリンタ出力範囲の原点Y（図面座標mm）。</summary>
    public double PrintOriginY { get; internal set; }

    /// <summary>プリンタ出力倍率。</summary>
    public double PrintScale { get; internal set; } = 1.0;

    /// <summary>一位: プリンタ90°回転出力、十位: 出力基準点位置（0:無指定、1〜9:テンキー配置）。</summary>
    public uint PrintFlags { get; internal set; }

    /// <summary>プリンタ90°回転出力が指定されているか。</summary>
    public bool PrintRotated => PrintFlags % 10 != 0;

    /// <summary>プリンタ出力基準点位置（0:無指定、1〜9: テンキー配置で 1=左下 … 9=右上）。</summary>
    public uint PrintBasePosition => PrintFlags / 10 % 10;

    /// <summary>色番号(0〜9)ごとの画面表示色（COLORREF: 0x00BBGGRR）。</summary>
    public uint[] ScreenPenColors { get; } = new uint[10];

    /// <summary>色番号(0〜9)ごとのプリンタ出力色（COLORREF: 0x00BBGGRR）。</summary>
    public uint[] PrinterPenColors { get; } = new uint[10];

    /// <summary>色番号(0〜9)ごとのプリンタ線幅（1/100mm 単位）。</summary>
    public uint[] PrinterPenWidths { get; } = new uint[10];

    /// <summary>色番号(0〜9)ごとのプリンタ実点半径（mm）。</summary>
    public double[] PrinterPointRadii { get; } = new double[10];

    /// <summary>線種番号2〜9のパターン（32bitのドットパターン）。インデックスは 線種番号-2。</summary>
    public uint[] LineTypePatterns { get; } = new uint[8];

    /// <summary>線種番号2〜9の1ユニットのドット数。</summary>
    public uint[] LineTypeUnitDots { get; } = new uint[8];

    /// <summary>線種番号2〜9のプリンタ出力ピッチ（1/100mm 単位）。</summary>
    public uint[] LineTypePrinterPitches { get; } = new uint[8];

    /// <summary>実点をプリンタ出力時に指定半径で書くか。</summary>
    public bool DrawPointsWithPrinterRadius { get; internal set; }

    /// <summary>カラー印刷指定。</summary>
    public bool ColorPrint { get; internal set; }

    /// <summary>SXF拡張線色(1〜257)のプリンタ出力色。インデックス0は未使用。</summary>
    public uint[] SxfPrinterPenColors { get; } = new uint[257];

    /// <summary>SXF拡張線色(1〜257)のプリンタ出力線幅（1/100mm 単位）。</summary>
    public uint[] SxfPrinterPenWidths { get; } = new uint[257];

    /// <summary>SXF拡張線種(1〜33)のセグメント数。</summary>
    public uint[] SxfLineTypeSegments { get; } = new uint[33];

    /// <summary>SXF拡張線種(1〜33)のピッチ（線分長・空白長の繰り返し、mm）。</summary>
    public double[,] SxfLineTypePitches { get; } = new double[33, 10];

    /// <summary>書込レイヤグループの縮尺の分母（一覧表示用）。</summary>
    public double ActiveScaleDenominator =>
        WriteGroup < 16 ? GroupScales[WriteGroup] : 100.0;

    /// <summary>指定したレイヤが印刷対象か（非表示レイヤ・非表示グループは印刷されない）。</summary>
    public bool IsLayerVisible(int group, int layer)
    {
        if ((uint)group >= 16 || (uint)layer >= 16) return true;
        return GroupStates[group] != 0 && LayerStates[group, layer] != 0;
    }

    /// <summary>
    /// ヘッダを読み込む。ar は "JwwData." シグネチャ直後を指していること。
    /// </summary>
    internal static JwwHeader Read(CArchiveReader ar)
    {
        var h = new JwwHeader();
        h.Version = ar.UInt32();
        if (h.Version is < 200 or > 3000)
            throw new JwwFormatException($"未知のデータバージョンです ({h.Version})。");
        uint v = h.Version;

        h.Memo = ar.String();
        h.PaperSizeCode = ar.UInt32();
        h.WriteGroup = ar.UInt32();

        for (int g = 0; g < 16; g++)
        {
            h.GroupStates[g] = ar.UInt32();
            ar.UInt32();                    // グループの書込レイヤ
            h.GroupScales[g] = ar.Double();
            ar.UInt32();                    // グループのプロテクト指定
            for (int l = 0; l < 16; l++)
            {
                h.LayerStates[g, l] = ar.UInt32();
                ar.UInt32();                // レイヤのプロテクト指定
            }
        }

        for (int i = 0; i < 14; i++) ar.UInt32(); // ダミー
        for (int i = 0; i < 5; i++) ar.UInt32();  // 寸法関係の設定 m_lnSunpou1〜5

        ar.UInt32();                        // ダミー
        ar.UInt32();                        // 線描画の最大幅 nWid
        h.PrintOriginX = ar.Double();
        h.PrintOriginY = ar.Double();
        h.PrintScale = ar.Double();
        h.PrintFlags = ar.UInt32();

        ar.UInt32();                        // 目盛モード
        ar.Double();                        // 目盛表示最小間隔
        ar.Double(); ar.Double();           // 目盛間隔 X, Y
        ar.Double(); ar.Double();           // 目盛基準点 X, Y

        for (int g = 0; g < 16; g++)
            for (int l = 0; l < 16; l++)
                h.LayerNames[g, l] = ar.String();
        for (int g = 0; g < 16; g++)
            h.GroupNames[g] = ar.String();

        ar.Double(); ar.Double();           // 日影 高度・緯度
        ar.UInt32();                        // 日影 9-15時フラグ
        ar.Double();                        // 壁面日影 倍率

        if (v >= 300)
        {
            ar.Double(); ar.Double();       // 天空図 測定レベル・円半径
        }
        ar.UInt32();                        // 2.5Dの計算単位

        ar.Double();                        // 保存時の画面倍率
        ar.Double(); ar.Double();           // 保存時の画面原点 X, Y
        ar.Double();                        // 範囲記憶倍率
        ar.Double(); ar.Double();           // 範囲記憶基準点 X, Y

        if (v >= 300)
        {
            for (int i = 0; i < 8; i++)
            {
                ar.Double(); ar.Double(); ar.Double(); // マークジャンプ倍率・基準点
                ar.UInt32();                           // マークジャンプレイヤグループ
            }
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                ar.Double(); ar.Double(); ar.Double();
            }
        }

        if (v >= 300)
        {
            ar.Double(); ar.Double(); ar.Double(); // ダミー
            ar.UInt32();                           // ダミー
            ar.Double(); ar.Double();              // ダミー
            ar.Double();                           // 文字背景範囲増寸法（4.04以前はダミー）
            ar.UInt32();                           // 文字背景描画フラグ
        }

        for (int i = 0; i < 10; i++) ar.Double(); // 複線間隔
        ar.Double();                              // 両側複線の留線出

        for (int i = 0; i < 10; i++)
        {
            h.ScreenPenColors[i] = ar.UInt32();
            ar.UInt32();                           // 画面表示線幅
        }
        for (int i = 0; i < 10; i++)
        {
            h.PrinterPenColors[i] = ar.UInt32();
            h.PrinterPenWidths[i] = ar.UInt32();
            h.PrinterPointRadii[i] = ar.Double();
        }

        for (int i = 0; i < 8; i++)
        {
            h.LineTypePatterns[i] = ar.UInt32();
            h.LineTypeUnitDots[i] = ar.UInt32();
            ar.UInt32();                           // 画面表示ピッチ
            h.LineTypePrinterPitches[i] = ar.UInt32();
        }
        for (int i = 0; i < 5; i++)                // ランダム線 1〜5
        {
            ar.UInt32(); ar.UInt32(); ar.UInt32(); ar.UInt32(); ar.UInt32();
        }
        for (int i = 0; i < 4; i++)                // 倍長線種 6〜9
        {
            ar.UInt32(); ar.UInt32(); ar.UInt32(); ar.UInt32();
        }

        ar.UInt32();                               // 実点を画面描画時の指定半径で描画
        h.DrawPointsWithPrinterRadius = ar.UInt32() != 0;
        ar.UInt32();                               // BitMap・ソリッドを最初に描画

        ar.UInt32(); ar.UInt32();                  // 逆描画・逆サーチ
        h.ColorPrint = ar.UInt32() != 0;
        ar.UInt32(); ar.UInt32();                  // レイヤ順印刷・色番号順印刷
        ar.UInt32();                               // プリンタ連続出力指定
        ar.UInt32();                               // 共通レイヤのグレー出力指定
        ar.UInt32();                               // 表示のみレイヤは出力しない（Ver.6以降はdpi）

        if (v >= 223)
        {
            ar.UInt32();                           // 作図時間
            ar.UInt32();                           // 2.5D視点初期化フラグ
            ar.UInt32(); ar.UInt32(); ar.UInt32(); // 2.5D視点水平角×3
            for (int i = 0; i < 5; i++) ar.Double(); // 2.5D視点 高さ・離れ等
        }
        if (v >= 225)
        {
            for (int i = 0; i < 4; i++) ar.Double(); // 各種指定の最終値
        }
        if (v >= 230)
        {
            ar.UInt32(); ar.UInt32();              // ソリッド任意色フラグ・既定値
        }

        if (v >= 420)
        {
            for (int i = 0; i <= 256; i++)         // SXF 画面表示色・線幅
            {
                ar.UInt32(); ar.UInt32();
            }
            for (int i = 0; i <= 256; i++)         // SXF プリンタ出力色
            {
                ar.String();                       // 線色名
                h.SxfPrinterPenColors[i] = ar.UInt32();
                h.SxfPrinterPenWidths[i] = ar.UInt32();
                ar.Double();                       // 点半径
            }
            for (int i = 0; i <= 32; i++)          // SXF 線種パターン
            {
                ar.UInt32(); ar.UInt32(); ar.UInt32(); ar.UInt32();
            }
            for (int i = 0; i <= 32; i++)          // SXF 線種パラメータ
            {
                ar.String();                       // 線種名
                h.SxfLineTypeSegments[i] = ar.UInt32();
                for (int j = 0; j < 10; j++)
                    h.SxfLineTypePitches[i, j] = ar.Double();
            }
        }

        for (int i = 0; i < 10; i++)               // 文字種1〜10
        {
            ar.Double(); ar.Double(); ar.Double(); ar.UInt32();
        }
        ar.Double(); ar.Double(); ar.Double();     // 書込み文字の幅・高さ・間隔
        ar.UInt32(); ar.UInt32();                  // 書込み文字の色番号・文字番号
        ar.Double(); ar.Double();                  // 文字位置整理の行間・文字数
        ar.UInt32();                               // 文字基準点ずれ使用フラグ
        for (int i = 0; i < 6; i++) ar.Double();   // 文字基準点ずれ X×3, Y×3

        return h;
    }
}
