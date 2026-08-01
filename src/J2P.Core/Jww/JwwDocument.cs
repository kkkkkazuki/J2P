using J2P.Core.Jww.Entities;

namespace J2P.Core.Jww;

/// <summary>解析済みのJWW図面。</summary>
public sealed class JwwDocument
{
    public JwwHeader Header { get; internal set; } = null!;

    /// <summary>図形データ（ファイル内の並び順＝Jw_cadの描画順）。</summary>
    public IReadOnlyList<JwwEntity> Entities => EntitiesInternal;

    /// <summary>ブロック定義（通し番号 → 定義）。</summary>
    public IReadOnlyDictionary<uint, JwwBlockDefinition> BlockDefinitions => BlockDefinitionsInternal;

    /// <summary>埋め込み画像の名前（Ver.7.00以降）。v1では描画対象外。</summary>
    public IReadOnlyList<string> EmbeddedImageNames => EmbeddedImageNamesInternal;

    /// <summary>解析中に発生した警告（未対応データのスキップ等）。</summary>
    public IReadOnlyList<string> Warnings => WarningsInternal;

    internal List<JwwEntity> EntitiesInternal { get; } = new();
    internal Dictionary<uint, JwwBlockDefinition> BlockDefinitionsInternal { get; } = new();
    internal List<string> EmbeddedImageNamesInternal { get; } = new();
    internal List<string> WarningsInternal { get; } = new();
}
