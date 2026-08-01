using System.Windows;
using J2P.App.ViewModels;

namespace J2P.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(App.Settings);
        DataContext = _viewModel;

        Width = App.Settings.WindowWidth;
        Height = App.Settings.WindowHeight;

        Closing += (_, _) =>
        {
            App.Settings.WindowWidth = Width;
            App.Settings.WindowHeight = Height;
            _viewModel.SaveSettings();
        };
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            _viewModel.AddPaths(paths);
        e.Handled = true;
    }
}
