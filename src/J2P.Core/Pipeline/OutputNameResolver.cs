namespace J2P.Core.Pipeline;

/// <summary>出力PDFのパス解決（命名ルール＋衝突処理）。</summary>
public static class OutputNameResolver
{
    /// <summary>命名ルールを適用したファイル名（衝突処理前、拡張子付き）を返す。</summary>
    public static string BuildFileName(string sourcePath, OutputSettings settings, DateTime now)
    {
        string name = Path.GetFileNameWithoutExtension(sourcePath);
        string result = settings.Naming switch
        {
            NamingRule.SourceNamePdf => $"{name}_PDF",
            NamingRule.SourceNameDate => $"{name}_{now:yyyyMMdd}",
            NamingRule.Custom => ApplyPattern(settings.CustomPattern, name, now),
            _ => name,
        };
        result = Sanitize(result);
        if (result.Length == 0) result = name.Length > 0 ? name : "output";
        return result + ".pdf";
    }

    /// <summary>出力先フォルダを返す。</summary>
    public static string BuildFolder(string sourcePath, OutputSettings settings) =>
        settings.Destination == DestinationMode.Folder && !string.IsNullOrWhiteSpace(settings.DestinationFolder)
            ? settings.DestinationFolder
            : Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;

    /// <summary>
    /// 衝突処理込みで出力パスを確定する。
    /// usedPaths には同一バッチ内で確定済みのパスを渡す（呼び出し側で共有）。
    /// 戻り値 null はスキップ（衝突ポリシー Skip）。
    /// </summary>
    public static string? Resolve(string sourcePath, OutputSettings settings, DateTime now,
        ISet<string> usedPaths, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        string folder = BuildFolder(sourcePath, settings);
        string fileName = BuildFileName(sourcePath, settings, now);
        string candidate = Path.Combine(folder, fileName);

        bool Taken(string p) =>
            usedPaths.Contains(p) || (settings.Collision != CollisionPolicy.Overwrite && fileExists(p));

        if (Taken(candidate))
        {
            switch (settings.Collision)
            {
                case CollisionPolicy.Skip:
                    return null;
                case CollisionPolicy.Overwrite:
                    // 既存ファイルは上書きするが、同一バッチ内での重複だけは連番で回避する
                    candidate = NextSequence(folder, fileName, Taken);
                    break;
                default:
                    candidate = NextSequence(folder, fileName, Taken);
                    break;
            }
        }

        usedPaths.Add(candidate);
        return candidate;
    }

    private static string NextSequence(string folder, string fileName, Func<string, bool> taken)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(folder, $"{stem}({i}).pdf");
            if (!taken(candidate)) return candidate;
        }
    }

    private static string ApplyPattern(string pattern, string name, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(pattern)) pattern = "{name}";
        return pattern
            .Replace("{name}", name, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", now.ToString("yyyyMMdd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", now.ToString("HHmmss"), StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Where(c => !invalid.Contains(c)).ToArray();
        return new string(chars).Trim();
    }
}
