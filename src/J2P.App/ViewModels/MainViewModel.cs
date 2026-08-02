using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using J2P.App.Services;
using J2P.Core.Jww;
using J2P.Core.Pipeline;
using Microsoft.Win32;

namespace J2P.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private PauseTokenSource? _pauseSource;
    private CancellationTokenSource? _cancelSource;
    private ConversionLog? _lastLog;
    private string? _lastOutputFolder;
    private string? _lastOutputPdf;
    private List<(ConversionJob Job, FileItemViewModel Item)> _runningJobs = new();

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        FilesView = CollectionViewSource.GetDefaultView(Files);
        FilesView.Filter = FilterItem;

        _darkMode = settings.DarkMode;
        _destinationIndex = settings.Destination == DestinationMode.Folder ? 1 : 0;
        _destinationFolder = settings.DestinationFolder;
        _namingIndex = (int)settings.Naming;
        _customPattern = settings.CustomPattern;
        _collisionIndex = (int)settings.Collision;
        _printAreaIndex = (int)settings.PrintArea;
        _paperIndex = (int)settings.Paper;
        _orientationIndex = (int)settings.Orientation;
        _magnificationIndex = settings.MagnificationPercent switch
        {
            0 => 0,
            100 => 1,
            70 => 2,
            50 => 3,
            _ => 4,
        };
        _customMagnification = settings.MagnificationPercent is not 0 and not 100 and not 70 and not 50
            ? settings.MagnificationPercent.ToString("0.#")
            : "80";
        _blackAndWhite = settings.BlackAndWhite;
        _includeSubfolders = settings.IncludeSubfolders;
        _onlyUpdated = settings.OnlyUpdated;
        _openFolderAfter = settings.OpenFolderAfter;
        _openPdfAfter = settings.OpenPdfAfter;
        _mergeToSinglePdf = settings.MergeToSinglePdf;
        _confirmBeforeConvert = settings.ConfirmBeforeConvert;
    }

    // ---- ファイル一覧 ----

    public ObservableCollection<FileItemViewModel> Files { get; } = new();
    public ICollectionView FilesView { get; }

    [ObservableProperty]
    private FileItemViewModel? _selectedFile;

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => FilesView.Refresh();

    private bool FilterItem(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        if (obj is not FileItemViewModel item) return false;
        return item.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || item.Folder.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    public string FileCountText => Files.Count == 0 ? "ファイルなし" : $"{Files.Count} ファイル";

    // ---- 設定（UIバインド用） ----

    [ObservableProperty]
    private bool _darkMode;

    partial void OnDarkModeChanged(bool value)
    {
        _settings.DarkMode = value;
        App.ApplyTheme(value);
        SaveSettings();
    }

    [ObservableProperty]
    private int _destinationIndex; // 0: 元と同じ / 1: 指定フォルダ

    [ObservableProperty]
    private string _destinationFolder = string.Empty;

    [ObservableProperty]
    private int _namingIndex; // NamingRule

    [ObservableProperty]
    private string _customPattern = "{name}";

    [ObservableProperty]
    private int _collisionIndex; // CollisionPolicy

    [ObservableProperty]
    private int _printAreaIndex; // PrintAreaMode

    [ObservableProperty]
    private int _paperIndex; // PaperSelection

    [ObservableProperty]
    private int _orientationIndex; // PaperOrientation

    [ObservableProperty]
    private int _magnificationIndex; // 0:自動 1:100 2:70 3:50 4:任意

    [ObservableProperty]
    private string _customMagnification = "80";

    [ObservableProperty]
    private bool _blackAndWhite;

    [ObservableProperty]
    private bool _includeSubfolders;

    [ObservableProperty]
    private bool _onlyUpdated;

    [ObservableProperty]
    private bool _openFolderAfter;

    [ObservableProperty]
    private bool _openPdfAfter;

    [ObservableProperty]
    private bool _mergeToSinglePdf;

    [ObservableProperty]
    private bool _confirmBeforeConvert;

    public bool IsCustomNaming => NamingIndex == (int)NamingRule.Custom;
    public bool IsCustomMagnification => MagnificationIndex == 4;
    public bool IsFolderDestination => DestinationIndex == 1;

    partial void OnDestinationIndexChanged(int value) { OnPropertyChanged(nameof(IsFolderDestination)); ApplySettings(); }
    partial void OnDestinationFolderChanged(string value) => ApplySettings();
    partial void OnNamingIndexChanged(int value) { OnPropertyChanged(nameof(IsCustomNaming)); ApplySettings(); }
    partial void OnCustomPatternChanged(string value) => ApplySettings();
    partial void OnCollisionIndexChanged(int value) => ApplySettings();
    partial void OnPrintAreaIndexChanged(int value) => ApplySettings();
    partial void OnPaperIndexChanged(int value) => ApplySettings();
    partial void OnOrientationIndexChanged(int value) => ApplySettings();
    partial void OnMagnificationIndexChanged(int value) { OnPropertyChanged(nameof(IsCustomMagnification)); ApplySettings(); }
    partial void OnCustomMagnificationChanged(string value) => ApplySettings();
    partial void OnBlackAndWhiteChanged(bool value) => ApplySettings();
    partial void OnIncludeSubfoldersChanged(bool value) => ApplySettings();
    partial void OnOnlyUpdatedChanged(bool value) => ApplySettings();
    partial void OnOpenFolderAfterChanged(bool value) => ApplySettings();
    partial void OnOpenPdfAfterChanged(bool value) => ApplySettings();
    partial void OnMergeToSinglePdfChanged(bool value) => ApplySettings();
    partial void OnConfirmBeforeConvertChanged(bool value) => ApplySettings();

    private void ApplySettings()
    {
        _settings.Destination = DestinationIndex == 1 ? DestinationMode.Folder : DestinationMode.SameAsSource;
        _settings.DestinationFolder = DestinationFolder;
        _settings.Naming = (NamingRule)Math.Clamp(NamingIndex, 0, 3);
        _settings.CustomPattern = CustomPattern;
        _settings.Collision = (CollisionPolicy)Math.Clamp(CollisionIndex, 0, 2);
        _settings.PrintArea = (Core.Pdf.PrintAreaMode)Math.Clamp(PrintAreaIndex, 0, 2);
        _settings.Paper = (Core.Pdf.PaperSelection)Math.Clamp(PaperIndex, 0, 5);
        _settings.Orientation = (Core.Pdf.PaperOrientation)Math.Clamp(OrientationIndex, 0, 2);
        _settings.MagnificationPercent = MagnificationIndex switch
        {
            1 => 100,
            2 => 70,
            3 => 50,
            4 => double.TryParse(CustomMagnification, out var v) && v > 0 ? v : 100,
            _ => 0,
        };
        _settings.BlackAndWhite = BlackAndWhite;
        _settings.IncludeSubfolders = IncludeSubfolders;
        _settings.OnlyUpdated = OnlyUpdated;
        _settings.OpenFolderAfter = OpenFolderAfter;
        _settings.OpenPdfAfter = OpenPdfAfter;
        _settings.MergeToSinglePdf = MergeToSinglePdf;
        _settings.ConfirmBeforeConvert = ConfirmBeforeConvert;
        SaveSettings();
        RefreshOutputNames();
    }

    public void SaveSettings() => SettingsService.Save(_settings);

    // ---- ファイル追加 ----

    [RelayCommand]
    private void AddFiles()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Jw_cad 図面 (*.jww)|*.jww|すべてのファイル (*.*)|*.*",
            Multiselect = true,
        };
        if (!string.IsNullOrEmpty(_settings.LastAddFolder))
            dialog.InitialDirectory = _settings.LastAddFolder;
        if (dialog.ShowDialog() == true)
        {
            AddPaths(dialog.FileNames);
            _settings.LastAddFolder = Path.GetDirectoryName(dialog.FileNames.FirstOrDefault() ?? "") ?? "";
            SaveSettings();
        }
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new OpenFolderDialog();
        if (!string.IsNullOrEmpty(_settings.LastAddFolder))
            dialog.InitialDirectory = _settings.LastAddFolder;
        if (dialog.ShowDialog() == true)
        {
            AddPaths(new[] { dialog.FolderName });
            _settings.LastAddFolder = dialog.FolderName;
            SaveSettings();
        }
    }

    /// <summary>ファイル/フォルダのパス群を一覧へ追加する（D&amp;D共用）。</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(Files.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
        var newItems = new List<FileItemViewModel>();

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var option = IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    foreach (var file in Directory.EnumerateFiles(path, "*.jww", option).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                        TryAdd(file);
                }
                else if (File.Exists(path) && path.EndsWith(".jww", StringComparison.OrdinalIgnoreCase))
                {
                    TryAdd(path);
                }
            }
            catch
            {
                // アクセスできないフォルダはスキップ
            }
        }

        foreach (var item in newItems)
            Files.Add(item);
        OnPropertyChanged(nameof(FileCountText));
        OnPropertyChanged(nameof(CanStart));
        RefreshOutputNames(newItems);
        _ = ScanHeadersAsync(newItems);

        void TryAdd(string file)
        {
            string full = Path.GetFullPath(file);
            if (existing.Add(full))
                newItems.Add(new FileItemViewModel(full));
        }
    }

    private async Task ScanHeadersAsync(List<FileItemViewModel> items)
    {
        foreach (var chunk in items.Chunk(16))
        {
            var results = await Task.Run(() => chunk.Select(item =>
            {
                try
                {
                    var header = JwwReader.ReadHeader(item.FullPath);
                    string paper = JwwPaperSizes.GetName(header.PaperSizeCode);
                    double denom = header.ActiveScaleDenominator;
                    string scale = denom switch
                    {
                        <= 0 => "-",
                        1.0 => "1/1",
                        _ => $"1/{denom:0.##}",
                    };
                    return (item, paper, scale);
                }
                catch
                {
                    return (item, paper: "読込不可", scale: "-");
                }
            }).ToList()).ConfigureAwait(true);

            foreach (var (item, paper, scale) in results)
            {
                item.PaperSize = paper;
                item.Scale = scale;
            }
        }
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedFile is { } item)
        {
            Files.Remove(item);
            OnPropertyChanged(nameof(FileCountText));
            OnPropertyChanged(nameof(CanStart));
        }
    }

    [RelayCommand]
    private void ClearFiles()
    {
        Files.Clear();
        OnPropertyChanged(nameof(FileCountText));
        OnPropertyChanged(nameof(CanStart));
    }

    [RelayCommand]
    private void BrowseDestinationFolder()
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            DestinationFolder = dialog.FolderName;
            DestinationIndex = 1;
        }
    }

    private void RefreshOutputNames(IReadOnlyList<FileItemViewModel>? target = null)
    {
        var settings = _settings.ToOutputSettings();
        var now = DateTime.Now;
        foreach (var item in target ?? (IReadOnlyList<FileItemViewModel>)Files)
            item.OutputName = OutputNameResolver.BuildFileName(item.FullPath, settings, now);
    }

    // ---- 変換 ----

    [ObservableProperty]
    private bool _isConverting;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _currentFileText = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _pauseButtonText = "一時停止";

    [ObservableProperty]
    private bool _showConfirmOverlay;

    [ObservableProperty]
    private string _confirmText = string.Empty;

    [ObservableProperty]
    private bool _showSummaryOverlay;

    [ObservableProperty]
    private string _summaryTitle = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    public bool CanStart => !IsConverting && Files.Count > 0;

    [RelayCommand]
    private void Start()
    {
        if (IsConverting || Files.Count == 0) return;

        if (ConfirmBeforeConvert)
        {
            ConfirmText = BuildConfirmText();
            ShowConfirmOverlay = true;
        }
        else
        {
            _ = StartCoreAsync();
        }
    }

    [RelayCommand]
    private void ConfirmProceed()
    {
        ShowConfirmOverlay = false;
        _ = StartCoreAsync();
    }

    [RelayCommand]
    private void ConfirmCancel() => ShowConfirmOverlay = false;

    private string BuildConfirmText()
    {
        var targets = FilesView.Cast<FileItemViewModel>().ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"対象ファイル: {targets.Count} 件");
        sb.AppendLine($"出力先: {(DestinationIndex == 1 ? DestinationFolder : "元ファイルと同じフォルダ")}");
        if (MergeToSinglePdf)
            sb.AppendLine("出力形式: 1つのPDFに結合");
        string paper = PaperIndex == 0 ? "自動" : ((Core.Pdf.PaperSelection)PaperIndex).ToString();
        sb.AppendLine($"用紙: {paper} / " +
                      $"{OrientationIndex switch { 1 => "縦", 2 => "横", _ => "向き自動" }} / " +
                      $"{(BlackAndWhite ? "白黒" : "カラー")}");
        if (OnlyUpdated) sb.AppendLine("更新されたファイルのみ変換");
        int overwriteRisk = 0;
        if (!MergeToSinglePdf)
        {
            var outSettings = _settings.ToOutputSettings();
            var now = DateTime.Now;
            foreach (var t in targets)
            {
                string p = Path.Combine(OutputNameResolver.BuildFolder(t.FullPath, outSettings),
                    OutputNameResolver.BuildFileName(t.FullPath, outSettings, now));
                if (File.Exists(p)) overwriteRisk++;
            }
            if (overwriteRisk > 0)
            {
                string action = (CollisionPolicy)CollisionIndex switch
                {
                    CollisionPolicy.Overwrite => "上書きされます",
                    CollisionPolicy.Sequence => "連番が付きます",
                    _ => "スキップされます",
                };
                sb.AppendLine($"同名PDFが {overwriteRisk} 件存在します（{action}）。");
            }
        }
        return sb.ToString().TrimEnd();
    }

    private async Task StartCoreAsync()
    {
        var targets = FilesView.Cast<FileItemViewModel>().ToList();
        if (targets.Count == 0) return;

        foreach (var f in Files) f.ResetStatus();

        _runningJobs = targets.Select(t => (new ConversionJob(t.FullPath), t)).ToList();
        var jobs = _runningJobs.Select(p => p.Item1).ToList();

        _pauseSource = new PauseTokenSource();
        _cancelSource = new CancellationTokenSource();
        IsConverting = true;
        IsPaused = false;
        PauseButtonText = "一時停止";
        ProgressValue = 0;
        ProgressText = $"0 / {jobs.Count}";
        CurrentFileText = "準備中…";
        OnPropertyChanged(nameof(CanStart));

        var progress = new Progress<BatchProgress>(p =>
        {
            ProgressValue = p.Total == 0 ? 0 : (double)p.Processed / p.Total * 100;
            ProgressText = $"{p.Processed} / {p.Total}　成功 {p.Succeeded}　エラー {p.Failed}" +
                           (p.Skipped > 0 ? $"　スキップ {p.Skipped}" : "") +
                           $"　残り {p.Remaining}";
            CurrentFileText = p.CurrentFile is null ? "" : Path.GetFileName(p.CurrentFile);
            foreach (var (job, item) in _runningJobs)
                item.UpdateFrom(job);
        });

        try
        {
            _lastLog = await BatchConverter.RunAsync(jobs, _settings.ToBatchOptions(), progress,
                _pauseSource.Token, _cancelSource.Token);
            _lastLog.ToolVersion = AppInfo.DisplayVersion;
        }
        finally
        {
            foreach (var (job, item) in _runningJobs)
                item.UpdateFrom(job);
            IsConverting = false;
            IsPaused = false;
            CurrentFileText = string.Empty;
            OnPropertyChanged(nameof(CanStart));
        }

        var okJob = jobs.FirstOrDefault(j => j.Status == ConversionStatus.Succeeded && j.OutputPath is not null);
        _lastOutputPdf = okJob?.OutputPath;
        _lastOutputFolder = okJob?.OutputPath is { } op ? Path.GetDirectoryName(op) : null;

        ShowSummary(_lastLog);

        if (OpenFolderAfter && _lastOutputFolder is not null)
            ShellService.OpenFolder(_lastOutputFolder);
        if (OpenPdfAfter && _lastOutputPdf is not null)
            ShellService.OpenFile(_lastOutputPdf);
    }

    private void ShowSummary(ConversionLog log)
    {
        SummaryTitle = log.FailedCount == 0
            ? $"変換完了 — 成功 {log.SuccessCount} 件"
            : $"変換完了 — 成功 {log.SuccessCount} 件 / 失敗 {log.FailedCount} 件";

        var sb = new StringBuilder();
        sb.AppendLine($"処理時間: {log.Duration.TotalSeconds:F1} 秒");
        if (log.SkippedCount > 0) sb.AppendLine($"スキップ: {log.SkippedCount} 件");
        if (log.CanceledCount > 0) sb.AppendLine($"中止: {log.CanceledCount} 件");

        var failed = log.Jobs.Where(j => j.Status == ConversionStatus.Failed).ToList();
        if (failed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("失敗一覧:");
            foreach (var j in failed)
                sb.AppendLine($"  {Path.GetFileName(j.SourcePath)} : {j.Message}");
        }

        var warned = log.Jobs.Where(j => j.Status == ConversionStatus.Succeeded && j.Warnings.Count > 0).ToList();
        if (warned.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("警告:");
            foreach (var j in warned)
                sb.AppendLine($"  {Path.GetFileName(j.SourcePath)} : {string.Join(" / ", j.Warnings)}");
        }

        SummaryText = sb.ToString().TrimEnd();
        ShowSummaryOverlay = true;
    }

    [RelayCommand]
    private void CloseSummary() => ShowSummaryOverlay = false;

    [RelayCommand]
    private void PauseResume()
    {
        if (_pauseSource is null) return;
        if (IsPaused)
        {
            _pauseSource.Resume();
            IsPaused = false;
            PauseButtonText = "一時停止";
        }
        else
        {
            _pauseSource.Pause();
            IsPaused = true;
            PauseButtonText = "再開";
        }
    }

    [RelayCommand]
    private void CancelConversion()
    {
        _cancelSource?.Cancel();
        _pauseSource?.Resume();
    }

    [RelayCommand]
    private void SaveLog()
    {
        if (_lastLog is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = "テキストファイル (*.txt)|*.txt",
            FileName = $"J2P_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dialog.FileName, _lastLog.ToText(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"ログの保存に失敗しました。\n{ex.Message}", "J2P",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (_lastOutputFolder is not null)
            ShellService.OpenFolder(_lastOutputFolder);
    }
}
