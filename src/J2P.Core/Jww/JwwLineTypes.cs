namespace J2P.Core.Jww;

/// <summary>
/// Jw_cad の線種（線種番号2〜9）ドットパターンを破線配列へ変換する。
/// </summary>
/// <remarks>
/// パターンは 32bit のドット列で、先頭（MSB）から <c>unitDots</c> ビットぶんが 1 周期。
/// 1ビットの長さは「プリンタ出力ピッチ × プリンタドット」で、ドットは 1/300 インチ。
/// この換算は Jw_cad が実際に出力した PDF の実測値で確認している
/// （線種2: パターン0x99999999・unitDots=4・ピッチ2 → 線0.339mm/空0.339mm、周期0.677mm）。
/// パターンは周期的に繰り返されるため、末尾と先頭が同じ状態なら 1 本の線分として結合する。
/// </remarks>
internal static class JwwLineTypes
{
    /// <summary>線種ピッチの基準となるプリンタ解像度（dpi）。</summary>
    /// <remarks>
    /// Ver.6.00以降のヘッダが持つ dpi は「線幅」の基準値であって線種ピッチ用ではないため、
    /// ピッチ側は Jw_cad 出力の実測に一致する 300dpi 固定とする。
    /// </remarks>
    private const double DotDpi = 300.0;

    /// <summary>1ドットの長さ（mm）。</summary>
    public static double DotLengthMm(uint printerPitch) =>
        Math.Max(printerPitch, 1) * 25.4 / DotDpi;

    /// <summary>
    /// ドットパターンを破線配列（mm、[線,空,線,空…]）へ変換する。
    /// 実線・解釈できないパターンは null。
    /// </summary>
    public static double[]? Decode(uint pattern, uint unitDots, uint printerPitch)
    {
        if (unitDots is 0 or > 32) return null;

        uint mask = unitDots == 32 ? uint.MaxValue : (1u << (int)unitDots) - 1;
        uint bits = pattern & mask;
        if (bits == mask || bits == 0) return null; // 全て描画／全て空白は実線扱い

        // MSB側から 描画(1)/空白(0) の連長を取る
        var runs = new List<(bool On, int Len)>();
        for (int i = (int)unitDots - 1; i >= 0; i--)
        {
            bool on = (bits & (1u << i)) != 0;
            if (runs.Count > 0 && runs[^1].On == on)
                runs[^1] = (on, runs[^1].Len + 1);
            else
                runs.Add((on, 1));
        }

        // 周期パターンなので末尾と先頭が同じ状態なら結合する（周期は unitDots のまま）
        if (runs.Count > 1 && runs[0].On == runs[^1].On)
        {
            runs[0] = (runs[0].On, runs[0].Len + runs[^1].Len);
            runs.RemoveAt(runs.Count - 1);
        }

        // 描画から始まるように回転（結合後は先頭と末尾の状態が必ず異なる）
        if (!runs[0].On)
        {
            var head = runs[0];
            runs.RemoveAt(0);
            runs.Add(head);
        }

        if (runs.Count < 2 || runs.Count % 2 != 0) return null;

        double dotMm = DotLengthMm(printerPitch);
        return runs.Select(r => r.Len * dotMm).ToArray();
    }
}
