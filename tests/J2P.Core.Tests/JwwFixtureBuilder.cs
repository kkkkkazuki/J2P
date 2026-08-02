using System.Text;

namespace J2P.Core.Tests;

/// <summary>
/// テスト用に最小限のJWWバイナリを生成するビルダー。
/// フォーマット文書（jwdatafmt.txt）の並びに従い、MFC CArchive と同じ
/// 文字列長エスケープ・オブジェクトタグ・クラス表を書き出す。
/// </summary>
public sealed class JwwFixtureBuilder
{
    private readonly MemoryStream _body = new();
    private readonly BinaryWriter _w;
    private readonly Encoding _cp932;

    private readonly List<(string ClassName, Action WriteBody)> _entities = new();
    private readonly List<(int Number, string Name, List<(string ClassName, Action WriteBody)> Children)> _blockDefs = new();

    public uint Version { get; init; } = 420;
    public uint PaperSizeCode { get; set; } = 3; // A3
    public uint WriteGroup { get; set; } = 0;
    public double[] GroupScales { get; } = Enumerable.Repeat(100.0, 16).ToArray();
    public uint[] GroupStates { get; } = Enumerable.Repeat(3u, 16).ToArray();
    public uint[,] LayerStates { get; } = CreateLayerStates();
    public string Memo { get; set; } = "";
    public double PrintOriginX { get; set; }
    public double PrintOriginY { get; set; }
    public double PrintScale { get; set; } = 1.0;
    public uint PrintFlags { get; set; }
    public uint[] PrinterPenColors { get; } = new uint[10];
    public uint[] PrinterPenWidths { get; } = new uint[10];

    /// <summary>線種2〜9のドットパターン（インデックスは 線種番号-2）。</summary>
    public uint[] LineTypePatterns { get; } = Enumerable.Repeat(0xAAAAAAAAu, 8).ToArray();
    /// <summary>線種2〜9の1ユニットのドット数。</summary>
    public uint[] LineTypeUnitDots { get; } = Enumerable.Repeat(8u, 8).ToArray();
    /// <summary>線種2〜9のプリンタ出力ピッチ。</summary>
    public uint[] LineTypePrinterPitches { get; } = Enumerable.Repeat(30u, 8).ToArray();

    private static uint[,] CreateLayerStates()
    {
        var a = new uint[16, 16];
        for (int g = 0; g < 16; g++)
            for (int l = 0; l < 16; l++)
                a[g, l] = 3;
        return a;
    }

    public JwwFixtureBuilder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _cp932 = Encoding.GetEncoding(932);
        _w = new BinaryWriter(_body);
        for (int i = 1; i < 10; i++)
        {
            PrinterPenColors[i] = 0x000000;
            PrinterPenWidths[i] = (uint)(i * 10); // 0.1mm 刻み
        }
    }

    // ---- 図形の追加 ----

    public JwwFixtureBuilder AddLine(double x0, double y0, double x1, double y1,
        ushort penColor = 1, byte penStyle = 1, ushort layer = 0, ushort glayer = 0)
    {
        _entities.Add(("CDataSen", () =>
        {
            WriteCommon(penStyle, penColor, layer, glayer);
            _w.Write(x0); _w.Write(y0); _w.Write(x1); _w.Write(y1);
        }
        ));
        return this;
    }

    public JwwFixtureBuilder AddArc(double cx, double cy, double radius,
        double startAngle = 0, double sweepAngle = Math.PI, double tilt = 0,
        double flatness = 1.0, bool fullCircle = false, ushort penColor = 1)
    {
        _entities.Add(("CDataEnko", () =>
        {
            WriteCommon(1, penColor, 0, 0);
            _w.Write(cx); _w.Write(cy); _w.Write(radius);
            _w.Write(startAngle); _w.Write(sweepAngle); _w.Write(tilt);
            _w.Write(flatness);
            _w.Write(fullCircle ? 1u : 0u);
        }
        ));
        return this;
    }

    public JwwFixtureBuilder AddPoint(double x, double y, bool temporary = false)
    {
        _entities.Add(("CDataTen", () =>
        {
            WriteCommon(1, 1, 0, 0);
            _w.Write(x); _w.Write(y);
            _w.Write(temporary ? 1u : 0u);
        }
        ));
        return this;
    }

    public JwwFixtureBuilder AddText(double x0, double y0, double x1, double y1,
        string text, double sizeX = 3, double sizeY = 3, double spacing = 0,
        double angleDeg = 0, string fontName = "ＭＳ ゴシック")
    {
        _entities.Add(("CDataMoji", () =>
        {
            WriteCommon(1, 1, 0, 0);
            _w.Write(x0); _w.Write(y0); _w.Write(x1); _w.Write(y1);
            _w.Write(1u); // 文字種
            _w.Write(sizeX); _w.Write(sizeY); _w.Write(spacing); _w.Write(angleDeg);
            WriteString(fontName);
            WriteString(text);
        }
        ));
        return this;
    }

    public JwwFixtureBuilder AddSolid(double x0, double y0, double x1, double y1,
        double x2, double y2, double x3, double y3, uint? rgb = null)
    {
        _entities.Add(("CDataSolid", () =>
        {
            WriteCommon(1, rgb.HasValue ? (ushort)10 : (ushort)1, 0, 0);
            _w.Write(x0); _w.Write(y0); _w.Write(x1); _w.Write(y1);
            _w.Write(x2); _w.Write(y2); _w.Write(x3); _w.Write(y3);
            if (rgb.HasValue) _w.Write(rgb.Value);
        }
        ));
        return this;
    }

    public JwwFixtureBuilder AddBlockInsert(double bx, double by, double sx, double sy,
        double rotation, uint defNumber)
    {
        _entities.Add(("CDataBlock", () =>
        {
            WriteCommon(1, 1, 0, 0);
            _w.Write(bx); _w.Write(by); _w.Write(sx); _w.Write(sy);
            _w.Write(rotation);
            _w.Write(defNumber);
        }
        ));
        return this;
    }

    /// <summary>線1本だけを含むブロック定義を追加する。</summary>
    public JwwFixtureBuilder AddBlockDefinitionWithLine(int number, string name,
        double x0, double y0, double x1, double y1)
    {
        var children = new List<(string, Action)>
        {
            ("CDataSen", () =>
            {
                WriteCommon(1, 1, 0, 0);
                _w.Write(x0); _w.Write(y0); _w.Write(x1); _w.Write(y1);
            }
            ),
        };
        _blockDefs.Add((number, name, children));
        return this;
    }

    // ---- バイナリ生成 ----

    public byte[] Build()
    {
        _body.SetLength(0);
        _classIndex.Clear();
        _nextLoadIndex = 1;

        _w.Write(Encoding.ASCII.GetBytes("JwwData."));
        WriteHeader();

        WriteCount((uint)_entities.Count);
        foreach (var (className, writeBody) in _entities)
        {
            WriteObjectTag(className);
            writeBody();
        }

        WriteCount((uint)_blockDefs.Count);
        foreach (var (number, name, children) in _blockDefs)
        {
            WriteObjectTag("CDataList");
            WriteCommon(1, 1, 0, 0);
            _w.Write(number);
            _w.Write(1); // 参照フラグ
            if (Version >= 700) { _w.Write(0u); _w.Write(0u); } // CTime 64bit
            else _w.Write(0u);                                  // CTime 32bit
            WriteString(name);
            WriteCount((uint)children.Count);
            foreach (var (childClass, writeChild) in children)
            {
                WriteObjectTag(childClass);
                writeChild();
            }
        }

        if (Version >= 700)
            _w.Write(0); // 埋め込み画像数

        return _body.ToArray();
    }

    public MemoryStream BuildStream() => new(Build());

    // ---- 内部処理 ----

    private readonly Dictionary<string, int> _classIndex = new();
    private int _nextLoadIndex = 1;

    private void WriteObjectTag(string className)
    {
        if (_classIndex.TryGetValue(className, out int index))
        {
            _w.Write((ushort)(index | 0x8000));
        }
        else
        {
            _w.Write((ushort)0xFFFF);
            _w.Write((ushort)Version); // schema
            var nameBytes = Encoding.ASCII.GetBytes(className);
            _w.Write((ushort)nameBytes.Length);
            _w.Write(nameBytes);
            _classIndex[className] = _nextLoadIndex;
            _nextLoadIndex++; // クラスのインデックス
        }
        _nextLoadIndex++;     // オブジェクト自身のインデックス
    }

    private void WriteCommon(byte penStyle, ushort penColor, ushort layer, ushort glayer)
    {
        _w.Write(0u);        // 曲線属性番号
        _w.Write(penStyle);
        _w.Write(penColor);
        if (Version >= 351) _w.Write((ushort)0); // 個別線幅
        _w.Write(layer);
        _w.Write(glayer);
        _w.Write((ushort)0); // 属性フラグ
    }

    private void WriteCount(uint count)
    {
        if (count < 0xFFFF)
        {
            _w.Write((ushort)count);
        }
        else
        {
            _w.Write((ushort)0xFFFF);
            _w.Write(count);
        }
    }

    private void WriteString(string s)
    {
        var bytes = _cp932.GetBytes(s);
        if (bytes.Length < 0xFF)
        {
            _w.Write((byte)bytes.Length);
        }
        else
        {
            _w.Write((byte)0xFF);
            if (bytes.Length < 0xFFFF)
            {
                _w.Write((ushort)bytes.Length);
            }
            else
            {
                _w.Write((ushort)0xFFFF);
                _w.Write((uint)bytes.Length);
            }
        }
        _w.Write(bytes);
    }

    private void WriteHeader()
    {
        _w.Write(Version);
        WriteString(Memo);
        _w.Write(PaperSizeCode);
        _w.Write(WriteGroup);

        for (int g = 0; g < 16; g++)
        {
            _w.Write(GroupStates[g]);
            _w.Write(0u);              // 書込レイヤ
            _w.Write(GroupScales[g]);
            _w.Write(0u);              // プロテクト
            for (int l = 0; l < 16; l++)
            {
                _w.Write(LayerStates[g, l]);
                _w.Write(0u);          // レイヤプロテクト
            }
        }

        for (int i = 0; i < 14; i++) _w.Write(0u); // ダミー
        for (int i = 0; i < 5; i++) _w.Write(0u);  // 寸法設定

        _w.Write(0u);                  // ダミー
        _w.Write(0u);                  // nWid
        _w.Write(PrintOriginX);
        _w.Write(PrintOriginY);
        _w.Write(PrintScale);
        _w.Write(PrintFlags);

        _w.Write(0u);                  // 目盛モード
        _w.Write(1.0);                 // 目盛表示最小間隔
        _w.Write(10.0); _w.Write(10.0);// 目盛間隔
        _w.Write(0.0); _w.Write(0.0);  // 目盛基準点

        for (int g = 0; g < 16; g++)
            for (int l = 0; l < 16; l++)
                WriteString($"G{g}L{l}");
        for (int g = 0; g < 16; g++)
            WriteString($"グループ{g}");

        _w.Write(0.0); _w.Write(0.0);  // 日影
        _w.Write(0u);
        _w.Write(0.0);

        if (Version >= 300)
        {
            _w.Write(0.0); _w.Write(50.0); // 天空図
        }
        _w.Write(0u);                  // 2.5D単位

        _w.Write(1.0);                 // 画面倍率
        _w.Write(0.0); _w.Write(0.0);  // 画面原点
        _w.Write(1.0);                 // 範囲記憶倍率
        _w.Write(0.0); _w.Write(0.0);  // 範囲記憶基準点

        if (Version >= 300)
        {
            for (int i = 0; i < 8; i++)
            {
                _w.Write(1.0); _w.Write(0.0); _w.Write(0.0);
                _w.Write(0u);
            }
            _w.Write(0.0); _w.Write(0.0); _w.Write(0.0); // ダミー
            _w.Write(0u);
            _w.Write(0.0); _w.Write(0.0);
            _w.Write(0.0);             // 文字背景範囲増
            _w.Write(0u);              // 文字背景フラグ
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                _w.Write(1.0); _w.Write(0.0); _w.Write(0.0);
            }
        }

        for (int i = 0; i < 10; i++) _w.Write(0.0); // 複線間隔
        _w.Write(0.0);

        for (int i = 0; i < 10; i++)
        {
            _w.Write(0u); _w.Write(1u); // 画面表示色・線幅
        }
        for (int i = 0; i < 10; i++)
        {
            _w.Write(PrinterPenColors[i]);
            _w.Write(PrinterPenWidths[i]);
            _w.Write(0.2);              // 実点半径
        }

        for (int i = 0; i < 8; i++)     // 線種2〜9
        {
            _w.Write(LineTypePatterns[i]);
            _w.Write(LineTypeUnitDots[i]);
            _w.Write(4u);               // 画面表示ピッチ
            _w.Write(LineTypePrinterPitches[i]);
        }
        for (int i = 0; i < 5; i++)     // ランダム線
        {
            _w.Write(0u); _w.Write(0u); _w.Write(0u); _w.Write(0u); _w.Write(0u);
        }
        for (int i = 0; i < 4; i++)     // 倍長線種
        {
            _w.Write(0u); _w.Write(0u); _w.Write(0u); _w.Write(0u);
        }

        _w.Write(0u); _w.Write(0u); _w.Write(0u); // 実点描画・プリンタ実点・BitMap先描画
        _w.Write(0u); _w.Write(0u);               // 逆描画・逆サーチ
        _w.Write(1u);                             // カラー印刷
        _w.Write(0u); _w.Write(0u); _w.Write(0u); // レイヤ順・色順・連続出力
        _w.Write(0u); _w.Write(0u);               // 共通レイヤグレー・表示のみ非出力

        if (Version >= 223)
        {
            _w.Write(0u);              // 作図時間
            _w.Write(0u);              // 視点初期化
            _w.Write(0u); _w.Write(0u); _w.Write(0u);
            for (int i = 0; i < 5; i++) _w.Write(0.0);
        }
        if (Version >= 225)
        {
            for (int i = 0; i < 4; i++) _w.Write(0.0);
        }
        if (Version >= 230)
        {
            _w.Write(0u); _w.Write(0xFFFFFFu);
        }

        if (Version >= 420)
        {
            for (int i = 0; i <= 256; i++)
            {
                _w.Write(0u); _w.Write(1u);
            }
            for (int i = 0; i <= 256; i++)
            {
                WriteString("");
                _w.Write(0u); _w.Write(10u);
                _w.Write(0.2);
            }
            for (int i = 0; i <= 32; i++)
            {
                _w.Write(0u); _w.Write(16u); _w.Write(1u); _w.Write(10u);
            }
            for (int i = 0; i <= 32; i++)
            {
                WriteString("");
                _w.Write(0u);
                for (int j = 0; j < 10; j++) _w.Write(0.0);
            }
        }

        for (int i = 0; i < 10; i++)   // 文字種
        {
            _w.Write(3.0); _w.Write(3.0); _w.Write(0.5); _w.Write(1u);
        }
        _w.Write(3.0); _w.Write(3.0); _w.Write(0.5);
        _w.Write(1u); _w.Write(1u);
        _w.Write(1.0); _w.Write(10.0);
        _w.Write(0u);
        for (int i = 0; i < 6; i++) _w.Write(0.0);
    }
}
