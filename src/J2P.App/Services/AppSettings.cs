using J2P.Core.Pdf;
using J2P.Core.Pipeline;

namespace J2P.App.Services;

/// <summary>永続化するアプリ設定（%APPDATA%\J2P\settings.json）。</summary>
public sealed class AppSettings
{
    public bool DarkMode { get; set; }

    // 出力設定
    public DestinationMode Destination { get; set; } = DestinationMode.SameAsSource;
    public string DestinationFolder { get; set; } = string.Empty;
    public NamingRule Naming { get; set; } = NamingRule.SourceName;
    public string CustomPattern { get; set; } = "{name}";
    public CollisionPolicy Collision { get; set; } = CollisionPolicy.Overwrite;

    // PDF設定
    public PrintAreaMode PrintArea { get; set; } = PrintAreaMode.FilePrintSettings;
    public PaperSelection Paper { get; set; } = PaperSelection.Auto;
    public PaperOrientation Orientation { get; set; } = PaperOrientation.Auto;
    /// <summary>0 = 自動。それ以外は %。</summary>
    public double MagnificationPercent { get; set; }
    public bool BlackAndWhite { get; set; }

    // オプション
    public bool IncludeSubfolders { get; set; } = true;
    public bool OnlyUpdated { get; set; }
    public bool OpenFolderAfter { get; set; }
    public bool OpenPdfAfter { get; set; }
    public bool MergeToSinglePdf { get; set; }
    public bool ConfirmBeforeConvert { get; set; } = true;

    // ウィンドウ
    public double WindowWidth { get; set; } = 1180;
    public double WindowHeight { get; set; } = 720;

    public string LastAddFolder { get; set; } = string.Empty;

    /// <summary>Core のオプションへ変換する。</summary>
    public BatchConvertOptions ToBatchOptions() => new()
    {
        Render = new PdfRenderOptions
        {
            PrintArea = PrintArea,
            Paper = Paper,
            Orientation = Orientation,
            Magnification = MagnificationPercent > 0 ? MagnificationPercent / 100.0 : null,
            Color = BlackAndWhite ? ColorMode.BlackAndWhite : ColorMode.Color,
        },
        Output = ToOutputSettings(),
        OnlyUpdated = OnlyUpdated,
        MergeToSinglePdf = MergeToSinglePdf,
    };

    public OutputSettings ToOutputSettings() => new()
    {
        Destination = Destination,
        DestinationFolder = DestinationFolder,
        Naming = Naming,
        CustomPattern = CustomPattern,
        Collision = Collision,
    };
}
