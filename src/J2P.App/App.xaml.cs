using System.Windows;
using J2P.App.Services;

namespace J2P.App;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = SettingsService.Load();
        ApplyTheme(Settings.DarkMode);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    /// <summary>ライト/ダークテーマを実行時に切り替える。</summary>
    public static void ApplyTheme(bool dark)
    {
        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dictionaries = Current.Resources.MergedDictionaries;
        // 先頭の辞書がテーマ（App.xamlの並びと対応）
        dictionaries[0] = new ResourceDictionary { Source = uri };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsService.Save(Settings);
        base.OnExit(e);
    }
}
