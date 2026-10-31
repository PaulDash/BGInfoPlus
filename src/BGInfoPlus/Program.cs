using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BGInfoPlus.Core;
using Microsoft.Win32;

namespace BGInfoPlus;

internal static class Program
{
    [STAThread]
    public static void Main() => new Application().Run(new Editor());
}

internal sealed class Editor : Window
{
    private readonly StackPanel fields = new();
    private readonly List<(InfoField Field, CheckBox Toggle)> choices = [];
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSteelBlue };
    private readonly TextBox customPath = new() { Text = @"C:\AzureData\CustomData.bin", Foreground = Brushes.Black, Margin = new Thickness(0, 6, 0, 6) };
    private readonly ComboBox fontSize = new() { ItemsSource = new[] { 16, 20, 24, 28, 32, 40 }, SelectedItem = 24, Width = 80, Foreground = Brushes.Black };
    private readonly Button apply = new() { Content = "Apply wallpaper", IsEnabled = false };
    private RenderTargetBitmap? bitmap;
    private bool loading;
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private bool hasProfile;
    private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BGInfoPlus");
    private string profileWarning = "";

    public Editor()
    {
        Title = "BGInfoPlus"; Width = 1120; Height = 760; MinWidth = 850; MinHeight = 600;
        Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)); Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock { Text = "BGInfoPlus", FontSize = 28, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Azure VM information, on your desktop", Foreground = Brushes.LightSteelBlue });
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        columns.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(columns);
        var left = new StackPanel { Margin = new Thickness(0, 0, 24, 12) };
        columns.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        left.Children.Add(new TextBlock { Text = "CustomData source", FontWeight = FontWeights.SemiBold });
        left.Children.Add(customPath);
        var browse = new Button { Content = "Choose file…" }; browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "CustomData files|*.bin;*.json;*.txt|All files|*.*" };
            if (dialog.ShowDialog(this) == true) customPath.Text = dialog.FileName;
        }; left.Children.Add(browse);
        var refresh = new Button { Content = "Refresh information", Margin = new Thickness(0, 8, 0, 16) };
        refresh.Click += async (_, _) => await RefreshAsync(); left.Children.Add(refresh);
        left.Children.Add(new TextBlock { Text = "Select fields", FontSize = 18, FontWeight = FontWeights.SemiBold });
        left.Children.Add(new TextBlock { Text = "CustomData fields start unchecked. Selected values become visible on the wallpaper.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10), Foreground = Brushes.LightSteelBlue });
        left.Children.Add(fields);
        var right = new DockPanel(); Grid.SetColumn(right, 1); columns.Children.Add(right);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        toolbar.Children.Add(new TextBlock { Text = "Text size  ", VerticalAlignment = VerticalAlignment.Center }); toolbar.Children.Add(fontSize);
        fontSize.SelectionChanged += (_, _) => UpdatePreview();
        apply.Margin = new Thickness(12, 0, 0, 0); apply.Click += (_, _) => Apply(); toolbar.Children.Add(apply);
        DockPanel.SetDock(toolbar, Dock.Top); right.Children.Add(toolbar);
        var hint = new TextBlock { Text = "Preview • solid background • upper-right layout\nApplies to the current user's wallpaper. Long values are clipped to fit.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10), Foreground = Brushes.LightSteelBlue };
        DockPanel.SetDock(hint, Dock.Bottom); right.Children.Add(hint);
        right.Children.Add(new Border { Background = Brushes.Black, Padding = new Thickness(1), Child = preview });
        LoadProfile();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (loading) return;
        loading = true; apply.IsEnabled = false; status.Text = "Reading local and Azure information…";
        if (choices.Count > 0) CaptureSelection();
        var messages = new List<string>();
        var values = new List<InfoField>();
        try { values.AddRange(SystemInfo.ReadLocal()); }
        catch (Exception e) { messages.Add("Local information: " + e.Message); }
        try { values.AddRange(await SystemInfo.ReadAzureAsync()); }
        catch (Exception e) { messages.Add("Azure metadata unavailable: " + e.Message); }
        try
        {
            var file = new FileInfo(customPath.Text);
            if (file.Length > 1024 * 1024) throw new IOException("CustomData file exceeds the 1 MiB reading limit.");
            values.AddRange(CustomData.Parse(await File.ReadAllTextAsync(file.FullName, Encoding.UTF8)));
        }
        catch (Exception e) { messages.Add("CustomData unavailable: " + e.Message); }
        fields.Children.Clear(); choices.Clear();
        foreach (var field in values)
        {
            var toggle = new CheckBox { Content = new TextBlock { Text = field.Label, TextWrapping = TextWrapping.Wrap },
                IsChecked = hasProfile ? selected.Contains(field.Key) : !field.Key.StartsWith("custom:", StringComparison.Ordinal),
                Margin = new Thickness(0, 5, 0, 5), Foreground = Brushes.White };
            toggle.Checked += (_, _) => UpdatePreview(); toggle.Unchecked += (_, _) => UpdatePreview();
            choices.Add((field, toggle)); fields.Children.Add(toggle);
        }
        loading = false; UpdatePreview();
        status.Text = string.Join("\n", messages.Prepend(profileWarning).Where(m => m.Length > 0));
        if (status.Text.Length == 0) status.Text = "Information refreshed. Select fields and apply when ready.";
    }

    private void UpdatePreview()
    {
        if (loading || choices.Count == 0) return;
        try
        {
            var width = Math.Max(800, (int)SystemParameters.PrimaryScreenWidth);
            var height = Math.Max(600, (int)SystemParameters.PrimaryScreenHeight);
            var canvas = new Canvas { Width = width, Height = height, Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), ClipToBounds = true };
            var block = new TextBlock { Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = (int)(fontSize.SelectedItem ?? 24),
                Width = width * .55, MaxHeight = height - 96, TextWrapping = TextWrapping.Wrap,
                Text = string.Join("\n", choices.Where(c => c.Toggle.IsChecked == true).Select(c => c.Field.Label + ": " + c.Field.Value)) };
            Canvas.SetRight(block, 48); Canvas.SetTop(block, 48); canvas.Children.Add(block);
            canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout();
            bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas); bitmap.Freeze();
            preview.Source = bitmap; apply.IsEnabled = choices.Any(c => c.Toggle.IsChecked == true);
        }
        catch (Exception e) { apply.IsEnabled = false; bitmap = null; status.Text = "Preview failed: " + e.Message; }
    }

    private void Apply()
    {
        if (bitmap is null) return;
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var path = Path.Combine(DataDirectory, "wallpaper.bmp");
            var encoder = new BmpBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
            if (!SystemParametersInfo(0x0014, 0, path, 0x0001 | 0x0002))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            status.Text = "Wallpaper applied.";
            try { SaveProfile(); }
            catch (Exception e) { status.Text += " Settings could not be saved: " + e.Message; }
        }
        catch (Exception e) { status.Text = "Could not apply wallpaper: " + e.Message; }
    }

    private void CaptureSelection()
    {
        // Retain selected keys that are temporarily unavailable (e.g. IMDS offline).
        foreach (var choice in choices)
            if (choice.Toggle.IsChecked == true) selected.Add(choice.Field.Key); else selected.Remove(choice.Field.Key);
        hasProfile = true;
    }

    private void SaveProfile()
    {
        CaptureSelection();
        var json = System.Text.Json.JsonSerializer.Serialize(new Profile(customPath.Text, (int)(fontSize.SelectedItem ?? 24), selected.ToArray()));
        var path = Path.Combine(DataDirectory, "settings.json");
        File.WriteAllText(path + ".tmp", json); File.Move(path + ".tmp", path, true);
    }

    private void LoadProfile()
    {
        try
        {
            var path = Path.Combine(DataDirectory, "settings.json");
            if (!File.Exists(path)) return;
            var profile = System.Text.Json.JsonSerializer.Deserialize<Profile>(File.ReadAllText(path));
            if (profile is null || profile.SelectedKeys is null) return;
            customPath.Text = profile.CustomDataPath;
            if (new[] { 16, 20, 24, 28, 32, 40 }.Contains(profile.FontSize)) fontSize.SelectedItem = profile.FontSize;
            selected.UnionWith(profile.SelectedKeys); hasProfile = true;
        }
        catch (Exception e) { profileWarning = "Saved settings could not be loaded: " + e.Message; }
    }

    private sealed record Profile(string CustomDataPath, int FontSize, string[] SelectedKeys);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);
}
