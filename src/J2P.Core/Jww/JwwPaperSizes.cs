namespace J2P.Core.Jww;

/// <summary>用紙サイズコードと寸法（mm）の対応。Jw_cadの用紙は横長。</summary>
public static class JwwPaperSizes
{
    /// <summary>用紙コード → (幅, 高さ) mm（横長）。未知のコードは A4 扱い。</summary>
    public static (double Width, double Height) GetSizeMm(uint code) => code switch
    {
        0 => (1189, 841),    // A0
        1 => (841, 594),     // A1
        2 => (594, 420),     // A2
        3 => (420, 297),     // A3
        4 => (297, 210),     // A4
        8 => (1682, 1189),   // 2A
        9 => (2378, 1682),   // 3A
        10 => (3364, 2378),  // 4A
        11 => (4756, 3364),  // 5A
        12 => (10000, 7071), // 10m
        13 => (50000, 35355),// 50m
        14 => (100000, 70711), // 100m
        _ => (297, 210),
    };

    /// <summary>一覧表示用の名称。</summary>
    public static string GetName(uint code) => code switch
    {
        0 => "A0",
        1 => "A1",
        2 => "A2",
        3 => "A3",
        4 => "A4",
        8 => "2A",
        9 => "3A",
        10 => "4A",
        11 => "5A",
        12 => "10m",
        13 => "50m",
        14 => "100m",
        _ => $"不明({code})",
    };
}
