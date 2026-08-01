using System.Diagnostics;
using System.IO;

namespace J2P.App.Services;

/// <summary>エクスプローラ・既定アプリ連携。</summary>
public static class ShellService
{
    public static void OpenFolder(string folderPath)
    {
        try
        {
            if (Directory.Exists(folderPath))
                Process.Start(new ProcessStartInfo(folderPath) { UseShellExecute = true });
        }
        catch
        {
            // 開けなくても処理結果には影響しない
        }
    }

    public static void OpenFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
