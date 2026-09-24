using System.IO;
using System.Windows;
using LineScope.App.Model;
using LineScope.App.Views;
using LineScope.Core;
using Microsoft.Win32;

namespace LineScope.App;

/// <summary>
/// Application entry: one <see cref="MainWindow"/> per document (image file or test pattern), plus the shared
/// inspector and test-pattern generator windows. The app quits when the last document window closes.
/// </summary>
public partial class App : Application
{
    public static new App Current => (App)Application.Current;

    private readonly List<MainWindow> _documentWindows = [];
    private InspectorWindow? _inspector;
    private TestPatternWindow? _generator;

    public const string OpenFilter =
        "Images|*.png;*.jpg;*.jpeg;*.jpe;*.jfif;*.tif;*.tiff;*.bmp;*.dib;*.gif;*.ico;*.wdp;*.jxr;*.heic;*.heif;*.webp;*.dds|All files|*.*";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var files = e.Args.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            NewDocumentWindow().Show();
            return;
        }
        foreach (var path in files) OpenFile(path, reuse: null);
    }

    public MainWindow NewDocumentWindow()
    {
        var window = new MainWindow();
        _documentWindows.Add(window);
        window.Closed += (_, _) =>
        {
            _documentWindows.Remove(window);
            if (_documentWindows.Count == 0) Shutdown();
        };
        return window;
    }

    /// <summary>Shows the Open dialog and opens every chosen file; the first one goes into <paramref name="reuse"/> if it is empty.</summary>
    public void ShowOpenDialog(MainWindow? reuse)
    {
        var dialog = new OpenFileDialog { Filter = OpenFilter, Multiselect = true, Title = "Open Image" };
        if (dialog.ShowDialog(reuse) != true) return;
        foreach (var path in dialog.FileNames)
        {
            OpenFile(path, reuse);
            reuse = null;
        }
    }

    public async void OpenFile(string path, MainWindow? reuse)
    {
        var window = reuse is { HasDocument: false } ? reuse : NewDocumentWindow();
        window.ShowLoading($"Opening {Path.GetFileName(path)}…");
        if (!window.IsVisible) window.Show();
        window.Activate();
        try
        {
            var document = await Task.Run(() => ImageDocument.Load(path));
            window.LoadDocument(document, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            window.ShowEmpty();
            MessageBox.Show(window, $"“{Path.GetFileName(path)}” couldn't be opened.\n\n{ex.Message}", "LineScope",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            // Keep the empty window if it is the only one, so the app doesn't quit.
            if (reuse != window && _documentWindows.Count > 1) window.Close();
        }
    }

    /// <summary>Generates a test pattern in the background and shows it in a new analysis window.</summary>
    public async void OpenPattern(TestPatternSpec spec)
    {
        var window = NewDocumentWindow();
        window.Title = spec.Title;
        window.ShowLoading($"Generating {spec.Kind.DisplayName()}…");
        window.Show();
        var document = await Task.Run(() => ImageDocument.FromPattern(spec));
        window.LoadDocument(document, spec.Title);
    }

    public void ShowInspector()
    {
        if (_inspector is null)
        {
            _inspector = new InspectorWindow();
            _inspector.Closed += (_, _) => _inspector = null;
            _inspector.Show();
        }
        _inspector.Activate();
    }

    public void ShowPatternGenerator(Window? owner)
    {
        if (_generator is null)
        {
            _generator = new TestPatternWindow();
            if (owner is not null)
            {
                _generator.WindowStartupLocation = WindowStartupLocation.Manual;
                _generator.Left = owner.Left + 60;
                _generator.Top = owner.Top + 60;
            }
            _generator.Closed += (_, _) => _generator = null;
            _generator.Show();
        }
        _generator.Activate();
    }
}
