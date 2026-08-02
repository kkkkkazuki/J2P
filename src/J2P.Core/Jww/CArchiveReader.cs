using System.Text;

namespace J2P.Core.Jww;

/// <summary>
/// MFC CArchive 互換のバイナリリーダ。
/// JWWファイルは MFC の CArchive シリアライズ（リトルエンディアン）で保存されており、
/// 文字列長の段階エスケープ・オブジェクトタグ・クラス表を同じ規則で読む必要がある。
/// 規則は MFC の公開仕様と Jw_cad 付属フォーマット文書に基づく独自実装。
/// </summary>
internal sealed class CArchiveReader
{
    // 健全性ガード（破損ファイルでのハング・OOM防止）
    private const int MaxStringLength = 16 * 1024 * 1024;
    private const uint MaxObjectCount = 50_000_000;

    private readonly BinaryReader _r;

    // MFCのロード配列: 新規クラスと各オブジェクトが同一のインデックス空間（1始まり）を消費する。
    // クラスのエントリにはクラス名、オブジェクトのエントリには null を入れる。
    private readonly List<string?> _loadArray = new() { null };

    private static readonly Encoding Cp932;

    static CArchiveReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp932 = Encoding.GetEncoding(932);
    }

    public CArchiveReader(Stream stream)
    {
        _r = new BinaryReader(stream);
    }

    public long Position => _r.BaseStream.Position;

    public byte Byte()
    {
        try { return _r.ReadByte(); }
        catch (EndOfStreamException e) { throw Truncated(e); }
    }

    public ushort UInt16()
    {
        try { return _r.ReadUInt16(); }
        catch (EndOfStreamException e) { throw Truncated(e); }
    }

    public uint UInt32()
    {
        try { return _r.ReadUInt32(); }
        catch (EndOfStreamException e) { throw Truncated(e); }
    }

    public int Int32()
    {
        try { return _r.ReadInt32(); }
        catch (EndOfStreamException e) { throw Truncated(e); }
    }

    public double Double()
    {
        try { return _r.ReadDouble(); }
        catch (EndOfStreamException e) { throw Truncated(e); }
    }

    public byte[] Bytes(int count)
    {
        var buf = _r.ReadBytes(count);
        if (buf.Length != count) throw Truncated(null);
        return buf;
    }

    public void Skip(long count)
    {
        var s = _r.BaseStream;
        if (s.CanSeek)
        {
            if (s.Position + count > s.Length) throw Truncated(null);
            s.Seek(count, SeekOrigin.Current);
        }
        else
        {
            for (long i = 0; i < count; i++) Byte();
        }
    }

    /// <summary>CObList::Serialize 等が書く要素数。WORD、0xFFFF なら DWORD へエスケープ。</summary>
    public uint Count()
    {
        ushort w = UInt16();
        uint count = w != 0xFFFF ? w : UInt32();
        if (count > MaxObjectCount)
            throw new JwwFormatException($"要素数が異常です ({count})。ファイルが破損している可能性があります。");
        return count;
    }

    /// <summary>
    /// CString の読み込み。長さは BYTE → (0xFF) WORD → (0xFFFF) DWORD の段階エスケープ。
    /// WORD 段で 0xFFFE は UNICODE マーカー（長さ列を読み直し、UTF-16LE として復号）。
    /// 通常の Jw_cad ファイルは CP932 バイト列。
    /// </summary>
    public string String()
    {
        int charSize = 1;
        long len = Byte();
        if (len == 0xFF)
        {
            ushort w = UInt16();
            if (w == 0xFFFE)
            {
                // UNICODE 文字列マーカー: 長さの段階を最初から読み直す
                charSize = 2;
                len = Byte();
                if (len == 0xFF)
                {
                    w = UInt16();
                    len = w != 0xFFFF ? w : UInt32();
                }
            }
            else
            {
                len = w != 0xFFFF ? w : UInt32();
            }
        }

        long byteLen = len * charSize;
        if (byteLen > MaxStringLength)
            throw new JwwFormatException($"文字列長が異常です ({byteLen} bytes)。ファイルが破損している可能性があります。");
        if (byteLen == 0) return string.Empty;

        var buf = Bytes((int)byteLen);
        return charSize == 2 ? Encoding.Unicode.GetString(buf) : Cp932.GetString(buf);
    }

    /// <summary>
    /// シリアライズされたオブジェクトの先頭タグを読み、クラス名を返す（NULLオブジェクトは null）。
    /// タグ規則:
    ///   0x0000            … NULL ポインタ
    ///   0xFFFF            … 新規クラス（schema WORD、名前長 WORD、ASCII名が続く）
    ///   0x7FFF            … DWORD タグへエスケープ（0x80000000 ビットがクラス参照）
    ///   0x8000 ビット付き … 既出クラス参照（下位15bitがロード配列インデックス）
    ///   その他            … 既出オブジェクト参照（JWWの図形データでは出現しない想定）
    /// 新規クラス・オブジェクトはロード配列のインデックスを1つずつ消費する。
    /// </summary>
    public string? ReadObjectClass()
    {
        ushort w = UInt16();
        if (w == 0x0000) return null;

        if (w == 0xFFFF)
        {
            UInt16(); // schema（クラスのバージョン番号）。読み飛ばす
            ushort nameLen = UInt16();
            if (nameLen > 256) throw new JwwFormatException("クラス名が長すぎます。");
            string name = Encoding.ASCII.GetString(Bytes(nameLen));
            _loadArray.Add(name);   // クラスのエントリ
            _loadArray.Add(null);   // このオブジェクト自身のエントリ
            return name;
        }

        uint classIndex;
        if (w == 0x7FFF)
        {
            uint d = UInt32();
            if ((d & 0x80000000u) == 0)
                throw new JwwFormatException("オブジェクトの後方参照には対応していません。");
            classIndex = d & 0x7FFFFFFFu;
        }
        else if ((w & 0x8000) != 0)
        {
            classIndex = (uint)(w & 0x7FFF);
        }
        else
        {
            throw new JwwFormatException("オブジェクトの後方参照には対応していません。");
        }

        if (classIndex == 0 || classIndex >= _loadArray.Count || _loadArray[(int)classIndex] is not string className)
            throw new JwwFormatException($"不正なクラス参照です (index={classIndex})。");
        _loadArray.Add(null); // このオブジェクト自身のエントリ
        return className;
    }

    private JwwFormatException Truncated(Exception? inner) =>
        inner is null
            ? new JwwFormatException($"ファイルが途中で終わっています (offset={Position})。")
            : new JwwFormatException($"ファイルが途中で終わっています (offset={Position})。", inner);
}
