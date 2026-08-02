using System.Text;

namespace J2P.Core.Pipeline;

/// <summary>一括変換の結果ログ。</summary>
public sealed class ConversionLog
{
    public DateTime StartedAt { get; internal set; }
    public DateTime FinishedAt { get; internal set; }
    public TimeSpan Duration => FinishedAt - StartedAt;
    public IReadOnlyList<ConversionJob> Jobs { get; internal set; } = Array.Empty<ConversionJob>();

    public int TotalCount => Jobs.Count;
    public int SuccessCount => Jobs.Count(j => j.Status == ConversionStatus.Succeeded);
    public int FailedCount => Jobs.Count(j => j.Status == ConversionStatus.Failed);
    public int SkippedCount => Jobs.Count(j => j.Status == ConversionStatus.Skipped);
    public int CanceledCount => Jobs.Count(j => j.Status is ConversionStatus.Canceled or ConversionStatus.Pending);

    /// <summary>保存用テキストを生成する。</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== J2P 一括PDF変換ログ ===");
        sb.AppendLine($"開始時刻   : {StartedAt:yyyy/MM/dd HH:mm:ss}");
        sb.AppendLine($"終了時刻   : {FinishedAt:yyyy/MM/dd HH:mm:ss}");
        sb.AppendLine($"処理時間   : {Duration.TotalSeconds:F1} 秒");
        sb.AppendLine($"対象ファイル: {TotalCount} 件");
        sb.AppendLine($"成功       : {SuccessCount} 件");
        sb.AppendLine($"失敗       : {FailedCount} 件");
        sb.AppendLine($"スキップ   : {SkippedCount} 件");
        if (CanceledCount > 0)
            sb.AppendLine($"中止       : {CanceledCount} 件");
        sb.AppendLine();

        var failed = Jobs.Where(j => j.Status == ConversionStatus.Failed).ToList();
        if (failed.Count > 0)
        {
            sb.AppendLine("--- 失敗一覧 ---");
            foreach (var j in failed)
                sb.AppendLine($"[失敗] {j.SourcePath} : {j.Message}");
            sb.AppendLine();
        }

        var skipped = Jobs.Where(j => j.Status == ConversionStatus.Skipped).ToList();
        if (skipped.Count > 0)
        {
            sb.AppendLine("--- スキップ一覧 ---");
            foreach (var j in skipped)
                sb.AppendLine($"[スキップ] {j.SourcePath} : {j.Message}");
            sb.AppendLine();
        }

        sb.AppendLine("--- 成功一覧 ---");
        foreach (var j in Jobs.Where(j => j.Status == ConversionStatus.Succeeded))
        {
            sb.AppendLine($"[成功] {j.SourcePath} -> {j.OutputPath}");
            foreach (var w in j.Warnings)
                sb.AppendLine($"    警告: {w}");
        }
        return sb.ToString();
    }

    public async Task SaveAsync(string path, CancellationToken ct = default) =>
        await File.WriteAllTextAsync(path, ToText(), Encoding.UTF8, ct).ConfigureAwait(false);
}
