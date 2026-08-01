using System.Reflection;
using PdfSharp.Fonts;

namespace J2P.Core.Pdf;

/// <summary>
/// 同梱の BIZ UDゴシック / BIZ UD明朝（SIL OFL）へ解決するフォントリゾルバ。
/// Windowsの MSゴシック等は .ttc（TrueType Collection）で PDFsharp が読めないため、
/// JWW内のフォント名をゴシック/明朝の2系統へマップして常に同じ出力を得る。
/// </summary>
public sealed class JapaneseFontResolver : IFontResolver
{
    public const string Gothic = "BIZUDGothic";
    public const string GothicBold = "BIZUDGothic-Bold";
    public const string Mincho = "BIZUDMincho";

    private static readonly Lazy<byte[]> GothicData = new(() => Load("BIZUDGothic-Regular.ttf"));
    private static readonly Lazy<byte[]> GothicBoldData = new(() => Load("BIZUDGothic-Bold.ttf"));
    private static readonly Lazy<byte[]> MinchoData = new(() => Load("BIZUDMincho-Regular.ttf"));

    private static readonly object InstallLock = new();
    private static bool _installed;

    /// <summary>プロセス全体にリゾルバを設定する（多重呼び出し可）。</summary>
    public static void Install()
    {
        lock (InstallLock)
        {
            if (_installed) return;
            GlobalFontSettings.FontResolver = new JapaneseFontResolver();
            _installed = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        bool mincho = familyName.Contains("明朝") ||
                      familyName.Contains("Mincho", StringComparison.OrdinalIgnoreCase) ||
                      familyName.Contains("Serif", StringComparison.OrdinalIgnoreCase);
        if (mincho)
            return new FontResolverInfo(Mincho);
        return new FontResolverInfo(bold ? GothicBold : Gothic);
    }

    public byte[]? GetFont(string faceName) => faceName switch
    {
        Gothic => GothicData.Value,
        GothicBold => GothicBoldData.Value,
        Mincho => MinchoData.Value,
        _ => null,
    };

    private static byte[] Load(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            throw new InvalidOperationException($"同梱フォント {fileName} が見つかりません。");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
