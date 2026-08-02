using System.Reflection;

namespace J2P.App;

/// <summary>実行中のビルドを identify するための情報。</summary>
public static class AppInfo
{
    /// <summary>バージョン番号（例: 0.2.0）。</summary>
    public static string Version { get; }

    /// <summary>ビルド識別子（例: 20260802-e81f7d6、手元ビルドは local）。</summary>
    public static string BuildStamp { get; }

    /// <summary>表示用のバージョン文字列（例: v0.2.0 (20260802-e81f7d6)）。</summary>
    public static string DisplayVersion => $"v{Version} ({BuildStamp})";

    /// <summary>ウィンドウタイトル。</summary>
    public static string WindowTitle => $"J2P — Jw_cad 一括PDF変換　{DisplayVersion}";

    static AppInfo()
    {
        // InformationalVersion は "0.2.0+20260802-e81f7d6" の形
        string raw = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0+unknown";
        int plus = raw.IndexOf('+');
        Version = plus >= 0 ? raw[..plus] : raw;
        BuildStamp = plus >= 0 && plus + 1 < raw.Length ? raw[(plus + 1)..] : "unknown";
    }
}
