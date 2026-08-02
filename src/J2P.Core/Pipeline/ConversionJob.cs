namespace J2P.Core.Pipeline;

/// <summary>1ファイルの変換状態。</summary>
public enum ConversionStatus
{
    Pending,
    Converting,
    Succeeded,
    Failed,
    /// <summary>スキップ（同名スキップ・未更新スキップ）。</summary>
    Skipped,
    Canceled,
}

/// <summary>1ファイル分の変換ジョブ。</summary>
public sealed class ConversionJob
{
    public ConversionJob(string sourcePath) => SourcePath = sourcePath;

    public string SourcePath { get; }

    /// <summary>確定した出力パス（結合モードでは結合先）。</summary>
    public string? OutputPath { get; internal set; }

    public ConversionStatus Status { get; internal set; } = ConversionStatus.Pending;

    /// <summary>失敗・スキップの理由。</summary>
    public string? Message { get; internal set; }

    /// <summary>変換時の警告（未対応要素など）。</summary>
    public IReadOnlyList<string> Warnings { get; internal set; } = Array.Empty<string>();
}
