using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using J2P.Core.Pipeline;

namespace J2P.App.ViewModels;

/// <summary>ファイル一覧の1行。</summary>
public sealed partial class FileItemViewModel : ObservableObject
{
    public FileItemViewModel(string path)
    {
        FullPath = Path.GetFullPath(path);
        FileName = Path.GetFileName(path);
        Folder = Path.GetDirectoryName(FullPath) ?? string.Empty;
        try
        {
            Modified = File.GetLastWriteTime(FullPath);
        }
        catch
        {
            Modified = DateTime.MinValue;
        }
    }

    public string FullPath { get; }
    public string FileName { get; }
    public string Folder { get; }
    public DateTime Modified { get; }

    /// <summary>用紙サイズ表示（ヘッダスキャン後に設定）。</summary>
    [ObservableProperty]
    private string _paperSize = "…";

    /// <summary>縮尺表示（例 1/100）。</summary>
    [ObservableProperty]
    private string _scale = "…";

    /// <summary>出力予定PDF名。</summary>
    [ObservableProperty]
    private string _outputName = string.Empty;

    [ObservableProperty]
    private ConversionStatus _status = ConversionStatus.Pending;

    [ObservableProperty]
    private string _statusText = "待機";

    [ObservableProperty]
    private string _message = string.Empty;

    public void UpdateFrom(ConversionJob job)
    {
        Status = job.Status;
        Message = job.Message ?? (job.Warnings.Count > 0 ? string.Join(" / ", job.Warnings) : string.Empty);
        StatusText = job.Status switch
        {
            ConversionStatus.Pending => "待機",
            ConversionStatus.Converting => "変換中…",
            ConversionStatus.Succeeded => job.Warnings.Count > 0 ? "成功（警告あり）" : "成功",
            ConversionStatus.Failed => "失敗",
            ConversionStatus.Skipped => "スキップ",
            ConversionStatus.Canceled => "中止",
            _ => string.Empty,
        };
    }

    public void ResetStatus()
    {
        Status = ConversionStatus.Pending;
        StatusText = "待機";
        Message = string.Empty;
    }
}
