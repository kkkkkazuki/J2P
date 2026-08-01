using J2P.Core.Jww;
using J2P.Core.Pdf;

namespace J2P.Core.Pipeline;

/// <summary>一括変換の全体オプション。</summary>
public sealed class BatchConvertOptions
{
    public PdfRenderOptions Render { get; set; } = new();
    public OutputSettings Output { get; set; } = new();

    /// <summary>更新されたファイルのみ変換（出力PDFが元より新しければスキップ）。</summary>
    public bool OnlyUpdated { get; set; }

    /// <summary>全ファイルを1つのPDFへ結合する。</summary>
    public bool MergeToSinglePdf { get; set; }

    /// <summary>結合PDFの出力パス（null なら自動命名）。</summary>
    public string? MergeOutputPath { get; set; }
}

/// <summary>進捗通知。</summary>
public sealed record BatchProgress(
    int Total,
    int Processed,
    int Succeeded,
    int Failed,
    int Skipped,
    string? CurrentFile)
{
    public int Remaining => Total - Processed;
}

/// <summary>複数JWWファイルを順番にPDF化するパイプライン。</summary>
public static class BatchConverter
{
    /// <summary>
    /// jobs を順に変換する。ファイル単位の失敗はジョブに記録され、バッチは継続する。
    /// キャンセル時は残りを Canceled にして正常復帰する。
    /// </summary>
    public static async Task<ConversionLog> RunAsync(
        IReadOnlyList<ConversionJob> jobs,
        BatchConvertOptions options,
        IProgress<BatchProgress>? progress = null,
        PauseToken pause = default,
        CancellationToken ct = default)
    {
        var log = new ConversionLog { StartedAt = DateTime.Now, Jobs = jobs };
        var usedPaths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        int processed = 0, succeeded = 0, failed = 0, skipped = 0;
        var mergeParts = options.MergeToSinglePdf ? new List<byte[]>() : null;

        void Report(string? current) =>
            progress?.Report(new BatchProgress(jobs.Count, processed, succeeded, failed, skipped, current));

        Report(null);

        foreach (var job in jobs)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await pause.WaitWhilePausedAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            job.Status = ConversionStatus.Converting;
            Report(job.SourcePath);

            try
            {
                await Task.Run(() => ConvertOne(job, options, usedPaths, mergeParts), ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                job.Status = ConversionStatus.Canceled;
                break;
            }
            catch (Exception ex)
            {
                job.Status = ConversionStatus.Failed;
                job.Message = FriendlyMessage(ex);
            }

            processed++;
            switch (job.Status)
            {
                case ConversionStatus.Succeeded: succeeded++; break;
                case ConversionStatus.Failed: failed++; break;
                case ConversionStatus.Skipped: skipped++; break;
            }
            Report(job.SourcePath);
        }

        foreach (var job in jobs)
        {
            if (job.Status is ConversionStatus.Pending or ConversionStatus.Converting)
                job.Status = ConversionStatus.Canceled;
        }

        // 結合モード: 集めたページを1ファイルへ
        if (mergeParts is { Count: > 0 } && !ct.IsCancellationRequested)
        {
            string mergePath = options.MergeOutputPath ?? BuildDefaultMergePath(jobs, options);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(mergePath)!);
                using var fs = File.Create(mergePath);
                PdfMerger.Merge(mergeParts, fs);
                foreach (var job in jobs)
                    if (job.Status == ConversionStatus.Succeeded)
                        job.OutputPath = mergePath;
            }
            catch (Exception ex)
            {
                foreach (var job in jobs)
                {
                    if (job.Status == ConversionStatus.Succeeded)
                    {
                        job.Status = ConversionStatus.Failed;
                        job.Message = $"結合PDFの保存に失敗: {FriendlyMessage(ex)}";
                    }
                }
            }
        }

        log.FinishedAt = DateTime.Now;
        Report(null);
        return log;
    }

    private static void ConvertOne(ConversionJob job, BatchConvertOptions options,
        HashSet<string> usedPaths, List<byte[]>? mergeParts)
    {
        if (!File.Exists(job.SourcePath))
        {
            job.Status = ConversionStatus.Failed;
            job.Message = "ファイルが見つかりません。";
            return;
        }

        string? outputPath = null;
        if (mergeParts is null)
        {
            outputPath = OutputNameResolver.Resolve(job.SourcePath, options.Output, DateTime.Now, usedPaths);
            if (outputPath is null)
            {
                job.Status = ConversionStatus.Skipped;
                job.Message = "同名のPDFが既に存在します。";
                return;
            }

            if (options.OnlyUpdated && File.Exists(outputPath) &&
                File.GetLastWriteTimeUtc(outputPath) >= File.GetLastWriteTimeUtc(job.SourcePath))
            {
                job.Status = ConversionStatus.Skipped;
                job.OutputPath = outputPath;
                job.Message = "更新されていないためスキップしました。";
                return;
            }
        }

        var doc = JwwReader.Read(job.SourcePath);

        if (mergeParts is not null)
        {
            using var ms = new MemoryStream();
            job.Warnings = PdfRenderer.RenderToStream(doc, options.Render, ms);
            mergeParts.Add(ms.ToArray());
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath!)!);
            using var fs = File.Create(outputPath!);
            job.Warnings = PdfRenderer.RenderToStream(doc, options.Render, fs);
            job.OutputPath = outputPath;
        }

        job.Status = ConversionStatus.Succeeded;
    }

    private static string BuildDefaultMergePath(IReadOnlyList<ConversionJob> jobs, BatchConvertOptions options)
    {
        string folder = jobs.Count > 0
            ? OutputNameResolver.BuildFolder(jobs[0].SourcePath, options.Output)
            : options.Output.DestinationFolder;
        return Path.Combine(folder, $"J2P_結合_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
    }

    private static string FriendlyMessage(Exception ex) => ex switch
    {
        JwwFormatException => $"JWWファイルの解析に失敗: {ex.Message}",
        IOException => $"ファイル入出力エラー: {ex.Message}",
        UnauthorizedAccessException => $"アクセスが拒否されました: {ex.Message}",
        _ => ex.Message,
    };
}
