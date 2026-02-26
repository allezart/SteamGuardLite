using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SteamGuardLite;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private string? _sharedSecret;
    private string _currentCode = "-----";

    private long _timeSlice = -1; // unix/30
    private string _baseTitle = "Steam Guard Lite";
    private DateTime _titleUntilUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        _baseTitle = Title;

        _timer.Tick += (_, _) => OnTick();
        _timer.Start();

        SetNotLoadedUi();
    }

    // Minimal window size based on current content
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        UpdateLayout();

        double w = ActualWidth;
        double h = ActualHeight;

        SizeToContent = SizeToContent.Manual;

        Width = w;
        Height = h;
        MinWidth = w;
        MinHeight = h;
    }

    private void AccountLoad_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "maFile / json|*.maFile;*.json|All files|*.*"
        };

        if (dlg.ShowDialog() == true)
            LoadFromPath(dlg.FileName);
    }

    private void GuardButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_sharedSecret))
            return;

        Clipboard.SetText(_currentCode);
        FlashTitle("Copied", 1.5);
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length > 0)
            LoadFromPath(files[0]);
    }

    private void LoadFromPath(string path)
    {
        try
        {
            var json = File.ReadAllText(path);

            var (ok, account, secret, error) = MafileReader.TryExtractAccountAndSecret(json);
            if (!ok)
            {
                MessageBox.Show(error, "maFile error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _sharedSecret = secret;
            _timeSlice = -1;

            GuardButton.IsEnabled = true;
            GuardProgress.IsEnabled = true;

            AccountText.Text = $"Account: {(!string.IsNullOrWhiteSpace(account) ? account : "(no account_name)")}";

            UpdateGuardNow();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "File read error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnTick()
    {
        if (Title != _baseTitle && DateTime.UtcNow > _titleUntilUtc)
            Title = _baseTitle;

        // Progress bar must not run until an account is loaded
        if (string.IsNullOrWhiteSpace(_sharedSecret))
        {
            GuardProgress.Value = 0;
            GuardProgress.IsEnabled = false;
            GuardProgress.ToolTip = "Load a maFile to see refresh progress";
            return;
        }

        GuardProgress.IsEnabled = true;

        var now = DateTimeOffset.UtcNow;
        long unix = now.ToUnixTimeSeconds();
        long slice = unix / 30;

        // New time slice => new code. Reset progress to 0 exactly here.
        if (slice != _timeSlice)
        {
            _timeSlice = slice;

            _currentCode = SteamGuardCodeGenerator.GenerateCode(_sharedSecret, unix);
            GuardButton.Content = _currentCode;

            GuardProgress.Value = 0;
            GuardProgress.ToolTip = "Refresh in: 30 s";
            return; // keep 0 visible for at least one tick
        }

        long ms = now.ToUnixTimeMilliseconds();
        double elapsedMs = ms % 30000.0;                  // 0..29999
        double progress = (elapsedMs / 30000.0) * 100.0;  // 0..100
        int remainSec = (int)Math.Ceiling((30000.0 - elapsedMs) / 1000.0);

        GuardProgress.Value = progress;
        GuardProgress.ToolTip = $"Refresh in: {remainSec} s";
    }

    private void UpdateGuardNow()
    {
        var now = DateTimeOffset.UtcNow;
        long unix = now.ToUnixTimeSeconds();

        _timeSlice = unix / 30;

        _currentCode = SteamGuardCodeGenerator.GenerateCode(_sharedSecret!, unix);
        GuardButton.Content = _currentCode;

        long ms = now.ToUnixTimeMilliseconds();
        double elapsedMs = ms % 30000.0;
        GuardProgress.Value = (elapsedMs / 30000.0) * 100.0;

        int remainSec = (int)Math.Ceiling((30000.0 - elapsedMs) / 1000.0);
        GuardProgress.ToolTip = $"Refresh in: {remainSec} s";
    }

    private void SetNotLoadedUi()
    {
        GuardButton.IsEnabled = false;
        GuardButton.Content = "-----";

        GuardProgress.Value = 0;
        GuardProgress.IsEnabled = false;
        GuardProgress.ToolTip = "Load a maFile to see refresh progress";

        AccountText.Text = "No account loaded";
        Title = _baseTitle;
    }

    private void FlashTitle(string title, double seconds)
    {
        Title = title;
        _titleUntilUtc = DateTime.UtcNow.AddSeconds(seconds);
    }
}