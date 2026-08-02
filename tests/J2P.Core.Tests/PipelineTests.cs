using J2P.Core.Pipeline;
using PdfSharp.Pdf.IO;
using Xunit;

namespace J2P.Core.Tests;

public class OutputNameResolverTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 10, 30, 45);

    [Theory]
    [InlineData(NamingRule.SourceName, "図面A.pdf")]
    [InlineData(NamingRule.SourceNamePdf, "図面A_PDF.pdf")]
    [InlineData(NamingRule.SourceNameDate, "図面A_20260801.pdf")]
    public void BuildFileName_命名ルール(NamingRule rule, string expected)
    {
        var settings = new OutputSettings { Naming = rule };
        Assert.Equal(expected, OutputNameResolver.BuildFileName("図面A.jww", settings, Now));
    }

    [Fact]
    public void BuildFileName_任意パターン()
    {
        var settings = new OutputSettings
        {
            Naming = NamingRule.Custom,
            CustomPattern = "{date}_{name}_提出用",
        };
        Assert.Equal("20260801_図面A_提出用.pdf",
            OutputNameResolver.BuildFileName("図面A.jww", settings, Now));
    }

    [Fact]
    public void Resolve_衝突時スキップ()
    {
        var settings = new OutputSettings { Collision = CollisionPolicy.Skip };
        var used = new HashSet<string>();
        var result = OutputNameResolver.Resolve("/src/a.jww", settings, Now, used, _ => true);
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_衝突時連番()
    {
        var settings = new OutputSettings { Collision = CollisionPolicy.Sequence };
        var used = new HashSet<string>();
        // 既存に a.pdf がある想定
        var result = OutputNameResolver.Resolve("/src/a.jww", settings, Now, used,
            p => Path.GetFileName(p) == "a.pdf");
        Assert.Equal("a(2).pdf", Path.GetFileName(result!));
    }

    [Fact]
    public void Resolve_同一バッチ内の重複も連番になる()
    {
        // 別フォルダの同名ファイルを同じ出力先へ集約するケース
        var settings = new OutputSettings
        {
            Destination = DestinationMode.Folder,
            DestinationFolder = "/out",
            Collision = CollisionPolicy.Overwrite,
        };
        var used = new HashSet<string>();
        var r1 = OutputNameResolver.Resolve("/src1/a.jww", settings, Now, used, _ => false);
        var r2 = OutputNameResolver.Resolve("/src2/a.jww", settings, Now, used, _ => false);
        Assert.Equal("a.pdf", Path.GetFileName(r1!));
        Assert.Equal("a(2).pdf", Path.GetFileName(r2!));
    }

    [Fact]
    public void Resolve_上書きは既存ファイルがあってもそのまま()
    {
        var settings = new OutputSettings { Collision = CollisionPolicy.Overwrite };
        var used = new HashSet<string>();
        var result = OutputNameResolver.Resolve("/src/a.jww", settings, Now, used, _ => true);
        Assert.Equal("a.pdf", Path.GetFileName(result!));
    }
}

public class BatchConverterTests : IDisposable
{
    private readonly string _dir;

    public BatchConverterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "j2p-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* テンポラリのため無視 */ }
    }

    private string WriteJww(string name, Action<JwwFixtureBuilder>? configure = null)
    {
        var b = new JwwFixtureBuilder();
        b.PrintOriginX = 0;
        b.PrintOriginY = 0;
        b.AddLine(0, 0, 100, 100);
        configure?.Invoke(b);
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, b.Build());
        return path;
    }

    [Fact]
    public async Task RunAsync_成功と失敗が混在してもバッチは完走する()
    {
        var ok = WriteJww("ok.jww");
        var broken = Path.Combine(_dir, "broken.jww");
        File.WriteAllBytes(broken, new byte[] { 1, 2, 3 });
        var missing = Path.Combine(_dir, "missing.jww");

        var jobs = new[] { new ConversionJob(ok), new ConversionJob(broken), new ConversionJob(missing) };
        var log = await BatchConverter.RunAsync(jobs, new BatchConvertOptions());

        Assert.Equal(ConversionStatus.Succeeded, jobs[0].Status);
        Assert.Equal(ConversionStatus.Failed, jobs[1].Status);
        Assert.Equal(ConversionStatus.Failed, jobs[2].Status);
        Assert.True(File.Exists(Path.Combine(_dir, "ok.pdf")));
        Assert.Equal(1, log.SuccessCount);
        Assert.Equal(2, log.FailedCount);
        Assert.Contains("ok.jww", log.ToText());
    }

    [Fact]
    public async Task RunAsync_進捗が通知される()
    {
        var jobs = new[] { new ConversionJob(WriteJww("a.jww")), new ConversionJob(WriteJww("b.jww")) };
        var reports = new List<BatchProgress>();
        var progress = new Progress<BatchProgress>(reports.Add);

        await BatchConverter.RunAsync(jobs, new BatchConvertOptions(), progress);
        await Task.Delay(100); // Progress<T> は同期コンテキスト経由のため少し待つ

        Assert.Contains(reports, r => r.Processed == 2 && r.Succeeded == 2);
    }

    [Fact]
    public async Task RunAsync_キャンセルで残りはCanceledになる()
    {
        var jobs = Enumerable.Range(0, 5)
            .Select(i => new ConversionJob(WriteJww($"c{i}.jww")))
            .ToArray();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var log = await BatchConverter.RunAsync(jobs, new BatchConvertOptions(), ct: cts.Token);

        Assert.All(jobs, j => Assert.Equal(ConversionStatus.Canceled, j.Status));
    }

    [Fact]
    public async Task RunAsync_一時停止中は進まず再開で完了する()
    {
        var jobs = Enumerable.Range(0, 3)
            .Select(i => new ConversionJob(WriteJww($"p{i}.jww")))
            .ToArray();
        var pts = new PauseTokenSource();
        pts.Pause();

        var run = BatchConverter.RunAsync(jobs, new BatchConvertOptions(), pause: pts.Token);
        await Task.Delay(300);
        Assert.Contains(jobs, j => j.Status == ConversionStatus.Pending); // 停止中

        pts.Resume();
        var log = await run.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(3, log.SuccessCount);
    }

    [Fact]
    public async Task RunAsync_未更新スキップ()
    {
        var src = WriteJww("inc.jww");
        var options = new BatchConvertOptions { OnlyUpdated = true };

        var log1 = await BatchConverter.RunAsync(new[] { new ConversionJob(src) }, options);
        Assert.Equal(1, log1.SuccessCount);

        // 2回目: PDFの方が新しいのでスキップ
        var jobs2 = new[] { new ConversionJob(src) };
        var log2 = await BatchConverter.RunAsync(jobs2, options);
        Assert.Equal(ConversionStatus.Skipped, jobs2[0].Status);
    }

    [Fact]
    public async Task RunAsync_結合モードで1つのPDFになる()
    {
        var jobs = new[]
        {
            new ConversionJob(WriteJww("m1.jww")),
            new ConversionJob(WriteJww("m2.jww")),
            new ConversionJob(WriteJww("m3.jww")),
        };
        string mergePath = Path.Combine(_dir, "merged.pdf");
        var options = new BatchConvertOptions
        {
            MergeToSinglePdf = true,
            MergeOutputPath = mergePath,
        };

        var log = await BatchConverter.RunAsync(jobs, options);

        Assert.Equal(3, log.SuccessCount);
        Assert.True(File.Exists(mergePath));
        using var pdf = PdfReader.Open(mergePath, PdfDocumentOpenMode.Import);
        Assert.Equal(3, pdf.PageCount);
        Assert.All(jobs, j => Assert.Equal(mergePath, j.OutputPath));
    }
}
