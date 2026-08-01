namespace J2P.Core.Pipeline;

/// <summary>出力先フォルダの決め方。</summary>
public enum DestinationMode
{
    /// <summary>元ファイルと同じフォルダへ保存。</summary>
    SameAsSource,
    /// <summary>指定フォルダへ保存。</summary>
    Folder,
}

/// <summary>出力ファイル名の命名ルール。</summary>
public enum NamingRule
{
    /// <summary>元ファイル名.pdf</summary>
    SourceName,
    /// <summary>元ファイル名_PDF.pdf</summary>
    SourceNamePdf,
    /// <summary>元ファイル名_日付.pdf</summary>
    SourceNameDate,
    /// <summary>任意パターン（{name} {date} {time} が使える）。</summary>
    Custom,
}

/// <summary>同名ファイルが存在した場合の動作。</summary>
public enum CollisionPolicy
{
    /// <summary>上書き。</summary>
    Overwrite,
    /// <summary>連番を付ける（name(2).pdf …）。</summary>
    Sequence,
    /// <summary>スキップ。</summary>
    Skip,
}

/// <summary>出力設定。</summary>
public sealed class OutputSettings
{
    public DestinationMode Destination { get; set; } = DestinationMode.SameAsSource;

    /// <summary>Destination が Folder のときの出力先。</summary>
    public string DestinationFolder { get; set; } = string.Empty;

    public NamingRule Naming { get; set; } = NamingRule.SourceName;

    /// <summary>Naming が Custom のときのパターン。{name}=元ファイル名、{date}=yyyyMMdd、{time}=HHmmss。</summary>
    public string CustomPattern { get; set; } = "{name}";

    public CollisionPolicy Collision { get; set; } = CollisionPolicy.Overwrite;
}
