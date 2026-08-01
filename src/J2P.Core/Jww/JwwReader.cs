using System.Text;
using J2P.Core.Jww.Entities;

namespace J2P.Core.Jww;

/// <summary>JWWファイルの読み込み。</summary>
public static class JwwReader
{
    private static readonly byte[] Signature = Encoding.ASCII.GetBytes("JwwData.");

    /// <summary>
    /// ヘッダのみを読み込む（一覧表示の用紙サイズ・縮尺取得用の軽量パス）。
    /// </summary>
    public static JwwHeader ReadHeader(Stream stream)
    {
        var ar = new CArchiveReader(stream);
        ReadSignature(ar);
        return JwwHeader.Read(ar);
    }

    /// <summary>ヘッダのみをファイルから読み込む。</summary>
    public static JwwHeader ReadHeader(string path)
    {
        using var fs = File.OpenRead(path);
        return ReadHeader(fs);
    }

    /// <summary>図面全体を読み込む。</summary>
    public static JwwDocument Read(Stream stream)
    {
        var ar = new CArchiveReader(stream);
        ReadSignature(ar);

        var doc = new JwwDocument
        {
            Header = JwwHeader.Read(ar),
        };
        uint version = doc.Header.Version;

        // 図形データ本体
        uint count = ar.Count();
        for (uint i = 0; i < count; i++)
        {
            var entity = ReadEntity(ar, version, doc, allowBlockDefinition: false);
            if (entity is not null)
                doc.EntitiesInternal.Add(entity);
        }

        // ブロック定義リスト
        uint defCount = ar.Count();
        for (uint i = 0; i < defCount; i++)
        {
            string? className = ar.ReadObjectClass();
            if (className is null) continue;
            if (className != "CDataList")
                throw new JwwFormatException($"ブロック定義リストに未知のクラス '{className}' があります。");
            var def = ReadBlockDefinition(ar, version, doc);
            doc.BlockDefinitionsInternal[(uint)def.Number] = def;
        }

        // 埋め込み画像（Ver.7.00以降）。v1では中身をスキップし、名前だけ控える。
        if (version >= 700)
        {
            int imageCount = ar.Int32();
            if (imageCount is < 0 or > 100_000)
                throw new JwwFormatException($"埋め込み画像数が異常です ({imageCount})。");
            for (int i = 0; i < imageCount; i++)
            {
                string name = ar.String();
                int size = ar.Int32();
                if (size is < 0)
                    throw new JwwFormatException($"埋め込み画像サイズが異常です ({size})。");
                ar.Skip(size);
                doc.EmbeddedImageNamesInternal.Add(name);
            }
            if (imageCount > 0)
                doc.WarningsInternal.Add($"埋め込み画像 {imageCount} 件はPDFに描画されません（未対応）。");
        }

        return doc;
    }

    /// <summary>図面全体をファイルから読み込む。</summary>
    public static JwwDocument Read(string path)
    {
        using var fs = File.OpenRead(path);
        return Read(fs);
    }

    private static void ReadSignature(CArchiveReader ar)
    {
        var buf = ar.Bytes(8);
        if (!buf.AsSpan().SequenceEqual(Signature))
            throw new JwwFormatException("JWWファイルではありません（シグネチャ不一致）。");
    }

    private static JwwEntity? ReadEntity(CArchiveReader ar, uint version, JwwDocument doc, bool allowBlockDefinition)
    {
        string? className = ar.ReadObjectClass();
        return className switch
        {
            null => null,
            "CDataSen" => JwwLine.Read(ar, version),
            "CDataEnko" => JwwArc.Read(ar, version),
            "CDataTen" => JwwPoint.Read(ar, version),
            "CDataMoji" => JwwText.Read(ar, version),
            "CDataSolid" => JwwSolid.Read(ar, version),
            "CDataSunpou" => JwwDimension.Read(ar, version),
            "CDataBlock" => JwwBlockInsert.Read(ar, version),
            "CDataList" when allowBlockDefinition => ReadBlockDefinition(ar, version, doc),
            _ => throw new JwwFormatException($"未知の図形クラス '{className}' が含まれています。"),
        };
    }

    private static JwwBlockDefinition ReadBlockDefinition(CArchiveReader ar, uint version, JwwDocument doc)
    {
        var def = new JwwBlockDefinition();
        def.ReadCommon(ar, version);
        def.Number = ar.Int32();
        def.IsReferenced = ar.Int32() != 0;

        // 作成時刻 (MFC CTime)。Ver.7.00以降のJw_cadは64bit time、それ以前は32bit。
        if (version >= 700) { ar.UInt32(); ar.UInt32(); }
        else ar.UInt32();

        def.Name = ar.String();

        uint count = ar.Count();
        for (uint i = 0; i < count; i++)
        {
            var child = ReadEntity(ar, version, doc, allowBlockDefinition: false);
            if (child is not null)
                def.EntitiesInternal.Add(child);
        }
        return def;
    }
}
