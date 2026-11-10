using System.Text.RegularExpressions;
using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Diagnostics;
using BGInfoPlus.Core;
using Microsoft.Win32;

namespace BGInfoPlus;

internal sealed class Editor : Window
{
    private readonly RichTextBox editor = new() { AcceptsTab = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0) };
    private readonly ListBox placeholders = new() { MinHeight = 120 };
    private readonly TextBlock status = new() { Foreground = UITheme.SecondaryTextBrush, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox runAtSignIn = new() { Content = "Apply at login", Foreground = UITheme.TextBrush, IsChecked = false, Margin = new Thickness(8, 8, 0, 0) };
    private readonly ComboBox resolutions = new() { Width = 170, Height = 34, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 4, 0) };
    private readonly TextBlock aspectRatio = new() { Foreground = UITheme.SecondaryTextBrush, HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox wallpaperFits = new() { Width = 170, Height = 34, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 4, 0), ItemsSource = Enum.GetValues<WallpaperFit>(), SelectedItem = WallpaperFit.Fill };
    private readonly List<DisplayResolution> availableResolutions = [];
    private readonly List<PlaceholderDefinition> definitions = [];
    private ConfigurationScope loadedScope = ConfigurationScope.BuiltIn;
    private readonly NativeColorPalette customColorPalette = new();
    private ComboBox? fontSizeControl;
    private double currentFontSize = 24;
    private Color wallpaperColor = Color.FromRgb(15, 23, 42);
    private readonly WindowsWallpaperConfiguration? launchWallpaperConfiguration;

    public Editor()
    {
        launchWallpaperConfiguration = WallpaperService.CaptureCurrentConfiguration();
        var availableHeight = Math.Max(620, SystemParameters.WorkArea.Height - 48);
        Title = "BGInfoPlus"; Width = 1180; Height = Math.Min(900, availableHeight); MinWidth = 1080; MinHeight = 620;
        Icon = ApplicationIcon.Create();
        UITheme.Apply(this);
        PopulateResolutions();
        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) }; Content = root;
        var header = new Grid { Margin = new Thickness(4, 0, 4, 20) };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "BGInfoPlus", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = UITheme.TextBrush });
        status.HorizontalAlignment = HorizontalAlignment.Right; status.VerticalAlignment = VerticalAlignment.Center;
        status.TextAlignment = TextAlignment.Right; status.MaxWidth = 620; status.Margin = new Thickness(24, 0, 0, 0);
        Grid.SetColumn(status, 1); header.Children.Add(status);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);

        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) }); columns.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(columns);
        var helperPanel = BuildHelperPanel(); columns.Children.Add(helperPanel);
        var design = new DockPanel();
        var designCard = new Border { Background = UITheme.CardBrush, BorderBrush = UITheme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Margin = new Thickness(16, 0, 0, 0), Child = design };
        Grid.SetColumn(designCard, 1); columns.Children.Add(designCard);
        var actions = BuildActions(); DockPanel.SetDock(actions, Dock.Bottom); design.Children.Add(actions);
        var formatting = BuildFormatting(); DockPanel.SetDock(formatting, Dock.Top); design.Children.Add(formatting);
        editor.Background = new SolidColorBrush(wallpaperColor); editor.Foreground = Brushes.White;
        UpdateEditorContrast(); editor.Padding = new Thickness(28); editor.SpellCheck.IsEnabled = false;
        design.Children.Add(new Border { BorderBrush = UITheme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = editor });
        LoadConfiguration();
    }

    private UIElement BuildHelperPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Image setup", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = UITheme.TextBrush });
        panel.Children.Add(new TextBlock { Text = "Output resolution", Foreground = UITheme.SecondaryTextBrush, Margin = new Thickness(0, 8, 0, 6) });
        resolutions.ToolTip = "Wallpaper output resolution";
        resolutions.SelectionChanged += (_, _) => UpdateAspectRatio();
        var resolutionRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        resolutionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        resolutionRow.ColumnDefinitions.Add(new ColumnDefinition());
        resolutionRow.Children.Add(resolutions);
        Grid.SetColumn(aspectRatio, 1); resolutionRow.Children.Add(aspectRatio);
        panel.Children.Add(resolutionRow);
        panel.Children.Add(new TextBlock { Text = "Wallpaper fit", Foreground = UITheme.SecondaryTextBrush, Margin = new Thickness(0, 0, 0, 6) });
        wallpaperFits.ToolTip = "How Windows fits the generated image to the desktop";
        wallpaperFits.Margin = new Thickness(0, 0, 0, 18);
        panel.Children.Add(wallpaperFits);
        UpdateAspectRatio();
        panel.Children.Add(new TextBlock { Text = "Insert data", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = UITheme.TextBrush });
        panel.Children.Add(new TextBlock { Text = "Choose a helper, then insert one or more named placeholders at the caret.", TextWrapping = TextWrapping.Wrap, Foreground = UITheme.SecondaryTextBrush, Margin = new Thickness(0, 4, 0, 16) });
        foreach (var (title, action) in new (string, Action)[]
        {
            ("Environment variables…", AddEnvironment), ("Registry…", AddRegistry), ("Networking interfaces…", AddNetworking),
            ("Azure metadata…", AddAzure),
            ("UserData JSON…", AddUserData), ("CIM system information…", AddCim)
        })
        {
            var button = new Button { Content = title, Margin = new Thickness(0, 0, 0, 8), HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => action(); panel.Children.Add(button);
        }
        var customData = new Button
        {
            Content = "CustomData…", IsEnabled = false, Margin = new Thickness(0, 0, 0, 8), HorizontalContentAlignment = HorizontalAlignment.Left,
            ToolTip = "CustomData is not implemented in this version."
        };
        ToolTipService.SetShowOnDisabled(customData, true); panel.Children.Add(customData);
        panel.Children.Add(new TextBlock { Text = "Placeholders", Margin = new Thickness(0, 16, 0, 8), FontSize = 16, FontWeight = FontWeights.SemiBold });
        placeholders.MouseDoubleClick += (_, _) => { if (placeholders.SelectedItem is PlaceholderDefinition item) InsertToken(item.Name); };
        panel.Children.Add(placeholders);
        var remove = new Button { Content = "Remove mapping", Margin = new Thickness(0, 7, 0, 12) };
        remove.Click += (_, _) => { if (placeholders.SelectedItem is PlaceholderDefinition item) { definitions.Remove(item); RefreshPlaceholderList(); } };
        panel.Children.Add(remove);
        return new Border
        {
            Background = UITheme.CardBrush, BorderBrush = UITheme.BorderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(16),
            Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
    }

    private UIElement BuildFormatting()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        AddCommandButton(bar, FormatIcon("B", FontWeights.Bold), "Bold (Ctrl+B)", EditingCommands.ToggleBold);
        AddCommandButton(bar, FormatIcon("I", FontWeights.Normal, FontStyles.Italic), "Italic (Ctrl+I)", EditingCommands.ToggleItalic);
        AddCommandButton(bar, FormatIcon("U", FontWeights.Normal, null, TextDecorations.Underline), "Underline (Ctrl+U)", EditingCommands.ToggleUnderline);
        AddSeparator(bar);
        AddCommandButton(bar, AlignmentIcon(TextAlignment.Left), "Align left (Ctrl+L)", EditingCommands.AlignLeft);
        AddCommandButton(bar, AlignmentIcon(TextAlignment.Center), "Center (Ctrl+E)", EditingCommands.AlignCenter);
        AddCommandButton(bar, AlignmentIcon(TextAlignment.Right), "Align right (Ctrl+R)", EditingCommands.AlignRight);
        AddSeparator(bar);
        bar.Children.Add(new TextBlock { Text = "A", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = UITheme.TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0), ToolTip = "Font size" });
        var sizes = new ComboBox
        {
            ItemsSource = new[] { 12, 16, 20, 24, 28, 32, 40, 48, 64 }, SelectedItem = 24,
            IsEditable = true, IsTextSearchEnabled = false, Text = "24",
            Width = 68, Height = 32, ToolTip = "Font size", VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 0, 4, 0)
        };
        fontSizeControl = sizes;
        sizes.SelectionChanged += (_, _) => { if (sizes.SelectedItem is int size) CommitFontSize(size.ToString(CultureInfo.CurrentCulture), true); };
        editor.SelectionChanged += (_, _) =>
        {
            var selectedSize = editor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            if (selectedSize is double size && size is >= 8 and <= 96)
            {
                currentFontSize = size;
                sizes.Text = size.ToString("0.##", CultureInfo.CurrentCulture);
            }
        };
        sizes.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            CommitFontSize(sizes.Text, true);
            e.Handled = true;
        };
        sizes.LostKeyboardFocus += (_, _) => CommitFontSize(sizes.Text, false);
        bar.Children.Add(sizes);
        AddToolbarButton(bar, FormatIcon("A", FontWeights.SemiBold), "Font color", ChooseFontColor);
        AddSeparator(bar);
        AddToolbarButton(bar, FormatIcon("▧", FontWeights.Normal), "Wallpaper color", ChooseWallpaperColor);
        return new Border
        {
            Child = bar, Background = UITheme.WindowBrush,
            BorderBrush = UITheme.BorderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 10)
        };
    }

    private void CommitFontSize(string text, bool returnFocus)
    {
        if (!TryParseFontSize(text, out var size))
        {
            status.Text = "Font size must be between 8 and 96 points.";
            if (fontSizeControl is not null)
                fontSizeControl.Text = currentFontSize.ToString("0.##", CultureInfo.CurrentCulture);
            return;
        }

        currentFontSize = size;
        if (fontSizeControl is not null)
            fontSizeControl.Text = currentFontSize.ToString("0.##", CultureInfo.CurrentCulture);
        editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        if (returnFocus) editor.Focus();
    }

    internal static bool TryParseFontSize(string text, out double size) =>
        double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out size) &&
        double.IsFinite(size) && size is >= 8 and <= 96;

    private void AddCommandButton(Panel panel, UIElement icon, string tooltip, RoutedUICommand command)
    {
        var button = new Button
        {
            Content = icon, ToolTip = tooltip, Command = command, CommandTarget = editor,
            Width = 34, Height = 32, Margin = new Thickness(0, 0, 3, 0), Padding = new Thickness(3)
        };
        panel.Children.Add(button);
    }

    private static void AddToolbarButton(Panel panel, UIElement icon, string tooltip, Action action)
    {
        var button = new Button { Content = icon, ToolTip = tooltip, Width = 34, Height = 32, Margin = new Thickness(0, 0, 3, 0), Padding = new Thickness(3) };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }

    private static TextBlock FormatIcon(string text, FontWeight weight, FontStyle? style = null, TextDecorationCollection? decorations = null) => new()
    {
        Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 16, FontWeight = weight,
        FontStyle = style ?? FontStyles.Normal, TextDecorations = decorations, Foreground = Brushes.Black,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };

    private static Canvas AlignmentIcon(TextAlignment alignment)
    {
        var canvas = new Canvas { Width = 18, Height = 16 };
        foreach (var (y, width) in new[] { (2d, 14d), (6d, 10d), (10d, 14d), (14d, 8d) })
        {
            var x = alignment switch { TextAlignment.Center => (18 - width) / 2, TextAlignment.Right => 16 - width, _ => 2 };
            canvas.Children.Add(new Line { X1 = x, X2 = x + width, Y1 = y, Y2 = y, Stroke = Brushes.Black, StrokeThickness = 1.5, SnapsToDevicePixels = true });
        }
        return canvas;
    }

    private static void AddSeparator(Panel panel) => panel.Children.Add(new Border
    {
        Width = 1, Height = 22, Background = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
        Margin = new Thickness(5, 5, 8, 5)
    });

    private UIElement BuildActions()
    {
        const double actionButtonWidth = 156;
        var bar = new Grid { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        for (var i = 0; i < 4; i++) bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var actionMargin = new Thickness(4, 0, 4, 0);
        var secondaryActionMargin = new Thickness(4, 6, 4, 0);
        var preview = new Button { Content = "Preview", Width = actionButtonWidth, Height = 34, Margin = actionMargin }; preview.Click += async (_, _) => await PreviewAsync();
        Grid.SetColumn(preview, 0); bar.Children.Add(preview);
        var apply = new Button { Content = "Apply now", Width = actionButtonWidth, Height = 34, Margin = actionMargin, Style = UITheme.AccentButtonStyle(), ToolTip = "Apply BGInfoPlus, or hold Alt while clicking to restore the wallpaper from before this window opened" }; apply.Click += async (_, _) => await ApplyAsync((Keyboard.Modifiers & ModifierKeys.Alt) != 0);
        Grid.SetColumn(apply, 1); bar.Children.Add(apply);
        var deploy = new Button { Content = "Apply to all users", Width = actionButtonWidth, Height = 34, Margin = actionMargin, ToolTip = "Save this design as the machine-wide template and apply it whenever any user signs in" };
        deploy.Click += async (_, _) => await DeployForAllUsersAsync(); Grid.SetColumn(deploy, 2); bar.Children.Add(deploy);
        var export = new Button { Content = "Export image…", Width = actionButtonWidth, Height = 34, Margin = actionMargin }; export.Click += async (_, _) => await ExportAsync();
        Grid.SetColumn(export, 3); bar.Children.Add(export);
        Grid.SetColumn(runAtSignIn, 1); Grid.SetRow(runAtSignIn, 1); bar.Children.Add(runAtSignIn);
        var importConfiguration = new Button { Content = "Import config…", Width = actionButtonWidth, Height = 34, Margin = secondaryActionMargin, ToolTip = "Load a JSON configuration into the editor without saving it" };
        importConfiguration.Click += (_, _) => ImportConfiguration(); Grid.SetColumn(importConfiguration, 2); Grid.SetRow(importConfiguration, 1); bar.Children.Add(importConfiguration);
        var exportConfiguration = new Button { Content = "Export config…", Width = actionButtonWidth, Height = 34, Margin = secondaryActionMargin, ToolTip = "Export this editable layout and its placeholder mappings as a JSON configuration" };
        exportConfiguration.Click += (_, _) => ExportConfiguration(); Grid.SetColumn(exportConfiguration, 3); Grid.SetRow(exportConfiguration, 1); bar.Children.Add(exportConfiguration);
        return bar;
    }

    private AppConfiguration CurrentConfiguration()
    {
        var selected = resolutions.SelectedItem as DisplayResolution ?? ScreenResolution.Current;
        var fit = wallpaperFits.SelectedItem is WallpaperFit selectedFit ? selectedFit : WallpaperFit.Fill;
        var configuration = new AppConfiguration(DocumentTools.Save(editor.Document), definitions.ToArray(), runAtSignIn.IsEnabled && runAtSignIn.IsChecked == true, selected.Width, selected.Height, ColorText(wallpaperColor), fit);
        AppLimits.Validate(configuration);
        return configuration;
    }

    private void PopulateResolutions()
    {
        var current = ScreenResolution.Current;
        availableResolutions.Add(current);
        foreach (var (width, height) in new[]
        {
            (800, 600), (1024, 768), (1152, 864), (1280, 720), (1280, 768), (1280, 800),
            (1280, 960), (1280, 1024), (1360, 768), (1366, 768), (1400, 1050), (1440, 900),
            (1600, 900), (1600, 1200), (1680, 1050), (1920, 1080), (1920, 1200)
        })
            if (width != current.Width || height != current.Height) availableResolutions.Add(new DisplayResolution(width, height));
        resolutions.ItemsSource = availableResolutions;
        resolutions.SelectedIndex = 0;
    }

    internal static string AspectRatioText(int width, int height)
    {
        if (width <= 0 || height <= 0) return string.Empty;
        // Some established display sizes are only approximately their marketed ratio
        // (1366 x 768, for example). Prefer the familiar label within 0.5 percent.
        foreach (var ratio in new (int Width, int Height)[] { (16, 9), (16, 10), (4, 3), (5, 4), (3, 2), (5, 3) })
        {
            var relativeDifference = Math.Abs((double)width / height - (double)ratio.Width / ratio.Height) /
                                     ((double)ratio.Width / ratio.Height);
            if (relativeDifference <= 0.005) return $"{ratio.Width}:{ratio.Height}";
        }
        var divisor = GreatestCommonDivisor(Math.Abs(width), Math.Abs(height));
        return $"{width / divisor}:{height / divisor}";
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0) (left, right) = (right, left % right);
        return left;
    }

    private void UpdateAspectRatio()
    {
        aspectRatio.Text = resolutions.SelectedItem is DisplayResolution resolution
            ? AspectRatioText(resolution.Width, resolution.Height)
            : string.Empty;
    }

    private void ChooseFontColor()
    {
        var current = editor.Selection.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush;
        var selected = ShowColorDialog(current?.Color ?? Colors.White);
        if (selected is not Color color) return;
        ApplyFontColor(color);
    }

    private void ChooseWallpaperColor()
    {
        var selected = ShowColorDialog(wallpaperColor);
        if (selected is not Color color) return;
        ApplyWallpaperColor(color);
    }

    private void ApplyFontColor(Color color)
    {
        var brush = new SolidColorBrush(color);
        editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
        editor.Focus();
    }

    private void ApplyWallpaperColor(Color color)
    {
        wallpaperColor = color;
        var brush = new SolidColorBrush(wallpaperColor);
        editor.Background = brush; UpdateEditorContrast();
    }

    private Color? ShowColorDialog(Color initial)
    {
        return NativeColorDialog.Show(new WindowInteropHelper(this).Handle, initial, customColorPalette);
    }

    private void UpdateEditorContrast()
    {
        var luminance = (0.299 * wallpaperColor.R + 0.587 * wallpaperColor.G + 0.114 * wallpaperColor.B) / 255;
        editor.CaretBrush = luminance > 0.55 ? Brushes.Black : Brushes.White;
    }

    private static string ColorText(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Color ParseColor(string value, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch { return fallback; }
    }

    private async Task PreviewAsync()
    {
        try { status.Text = "Resolving available data…"; var bitmap = await WallpaperService.RenderAsync(CurrentConfiguration()); new PreviewWindow(bitmap) { Owner = this }.ShowDialog(); status.Text = "Preview generated."; }
        catch (Exception e) { status.Text = "Preview could not be generated: " + e.Message; }
    }

    private async Task ApplyAsync(bool restoreLaunchWallpaper)
    {
        try
        {
            if (restoreLaunchWallpaper)
            {
                if (launchWallpaperConfiguration is null)
                    throw new InvalidOperationException("The wallpaper configuration from before launch could not be read.");
                WallpaperService.RestoreConfiguration(launchWallpaperConfiguration);
                status.Text = "Restored the wallpaper configuration from before BGInfoPlus was opened.";
                return;
            }
            var configuration = CurrentConfiguration(); ConfigurationStore.SaveUser(configuration);
            status.Text = "Applying wallpaper…"; await WallpaperService.ApplyAsync(configuration);
            status.Text = loadedScope == ConfigurationScope.AllUsers
                ? "Wallpaper applied and a personal copy was saved, but the enforced all-users configuration will be used at login. Use Deploy for all users to change it."
                : $"Wallpaper applied and configuration saved to {ConfigurationStore.UserPath}.";
        }
        catch (Exception e) { status.Text = "Wallpaper could not be applied: " + e.Message; }
    }

    private void ExportConfiguration()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export BGInfoPlus configuration", FileName = "BGInfoPlus-config.json",
                DefaultExt = ".json", AddExtension = true, Filter = "BGInfoPlus configuration|*.json|All files|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;
            // Startup behavior is local policy, not portable layout data.
            ConfigurationFileStore.SaveShared(dialog.FileName, CurrentConfiguration());
            status.Text = $"Configuration exported to {dialog.FileName}";
        }
        catch (Exception e) { status.Text = "Configuration could not be exported: " + e.Message; }
    }

    private void ImportConfiguration()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import BGInfoPlus configuration",
                DefaultExt = ".json",
                Filter = "BGInfoPlus configuration|*.json|All files|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;
            var configuration = ConfigurationFileStore.Load(dialog.FileName);
            var scope = File.Exists(ConfigurationStore.AllUsersPath) ? ConfigurationScope.AllUsers : ConfigurationScope.Explicit;
            ApplyConfigurationToEditor(configuration, scope, selectCurrentResolution: false);
            if (scope == ConfigurationScope.AllUsers)
            {
                runAtSignIn.IsChecked = true;
                runAtSignIn.IsEnabled = false;
                runAtSignIn.Content = "Applied at login by machine task";
                runAtSignIn.ToolTip = "The installed machine task applies the enforced all-users configuration.";
            }
            status.Text = scope == ConfigurationScope.AllUsers
                ? $"Configuration imported from {dialog.FileName}. It can be previewed, but the enforced all-users configuration still controls login."
                : $"Configuration imported from {dialog.FileName}. Use Apply wallpaper to save it.";
        }
        catch (Exception e) { status.Text = "Configuration could not be imported: " + e.Message; }
    }

    private async Task ExportAsync()
    {
        try
        {
            var configuration = CurrentConfiguration();
            var dialog = new SaveFileDialog
            {
                Title = "Export generated wallpaper",
                FileName = $"BGInfoPlus-{configuration.ImageWidth}x{configuration.ImageHeight}.png",
                DefaultExt = ".png",
                AddExtension = true,
                Filter = "PNG image|*.png|Bitmap image|*.bmp|JPEG image|*.jpg;*.jpeg"
            };
            if (dialog.ShowDialog(this) != true) return;
            status.Text = "Resolving data and exporting image…";
            var bitmap = await WallpaperService.RenderAsync(configuration);
            BitmapEncoder encoder = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
            {
                ".bmp" => new BmpBitmapEncoder(),
                ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                _ => new PngBitmapEncoder()
            };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            AtomicFile.Write(dialog.FileName, encoder.Save);
            status.Text = $"Image exported to {dialog.FileName}";
        }
        catch (Exception e) { status.Text = "Image could not be exported: " + e.Message; }
    }

    private Task DeployForAllUsersAsync() => DeployMachineConfigurationAsync();

    private async Task DeployMachineConfigurationAsync()
    {
        string? stagingPath = null;
        try
        {
            var script = System.IO.Path.Combine(AppContext.BaseDirectory, "Deploy-BGInfoPlusForAllUsers.ps1");
            if (!File.Exists(script))
                throw new FileNotFoundException("The all-users deployment helper is not installed. Use the portable ZIP or Azure VM Application package instead of the standalone executable.", script);
            stagingPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"BGInfoPlus-{Guid.NewGuid():N}.json");
            ConfigurationFileStore.SaveShared(stagingPath, CurrentConfiguration());
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe", UseShellExecute = true, Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal
            };
            start.ArgumentList.Add("-NoLogo"); start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-ExecutionPolicy"); start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File"); start.ArgumentList.Add(script);
            start.ArgumentList.Add("-TemplatePath"); start.ArgumentList.Add(stagingPath);
            start.ArgumentList.Add("-Force");
            start.ArgumentList.Add("-PauseOnError");
            status.Text = "Waiting for the elevated all-users deployment…";
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The deployment helper could not be started.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException($"The deployment helper exited with code {process.ExitCode}. See its PowerShell window for details.");
            runAtSignIn.IsChecked = true; runAtSignIn.IsEnabled = false;
            runAtSignIn.Content = "Applied at login by machine task";
            loadedScope = ConfigurationScope.AllUsers;
            status.Text = "All-users configuration deployed. BGInfoPlus will enforce it at every user logon.";
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            status.Text = "Machine configuration deployment was cancelled.";
        }
        catch (Exception e) { status.Text = "Machine configuration deployment failed: " + e.Message; }
        finally { if (stagingPath is not null) try { File.Delete(stagingPath); } catch { } }
    }

    private void LoadConfiguration()
    {
        try
        {
            var resolved = ConfigurationStore.LoadForEditor();
            ApplyConfigurationToEditor(resolved.Configuration, resolved.Scope, selectCurrentResolution: true);
            var machineLogon = resolved.Scope == ConfigurationScope.AllUsers;
            var configuration = resolved.Configuration;
            runAtSignIn.IsChecked = machineLogon || configuration.RunAtSignIn;
            runAtSignIn.IsEnabled = !machineLogon;
            runAtSignIn.Content = machineLogon ? "Applied at login by machine task" : "Always apply at login";
            runAtSignIn.ToolTip = machineLogon ? "The installed machine task applies the effective configuration for every interactive user." : null;
            status.Text = resolved.Scope switch
            {
                ConfigurationScope.User => $"Loaded your configuration from {resolved.Path}.",
                ConfigurationScope.AllUsers => $"Loaded the enforced all-users configuration from {resolved.Path}.",
                ConfigurationScope.Default => $"Loaded the application default from {resolved.Path} because no personal configuration exists.",
                _ => "Using the built-in starter configuration."
            };
        }
        catch (Exception e) { editor.Document = DocumentTools.NewDocument(); status.Text = "Configuration could not be loaded: " + e.Message; }
    }

    private void ApplyConfigurationToEditor(AppConfiguration configuration, ConfigurationScope scope, bool selectCurrentResolution)
    {
        AppLimits.Validate(configuration);
        loadedScope = scope;
        editor.Document = DocumentTools.Load(configuration.Document);
        definitions.Clear();
        definitions.AddRange(configuration.Placeholders);
        if (scope != ConfigurationScope.AllUsers)
        {
            runAtSignIn.IsEnabled = true;
            runAtSignIn.IsChecked = configuration.RunAtSignIn;
            runAtSignIn.Content = "Always apply at login";
            runAtSignIn.ToolTip = null;
        }
        wallpaperColor = ParseColor(configuration.BackgroundColor, wallpaperColor);
        var selectedResolution = availableResolutions[0];
        if (!selectCurrentResolution)
        {
            selectedResolution = availableResolutions.FirstOrDefault(r => r.Width == configuration.ImageWidth && r.Height == configuration.ImageHeight)!;
            if (selectedResolution is null)
            {
                selectedResolution = new DisplayResolution(configuration.ImageWidth, configuration.ImageHeight, "Imported");
                availableResolutions.Add(selectedResolution);
                resolutions.Items.Refresh();
            }
        }
        resolutions.SelectedItem = selectedResolution;
        wallpaperFits.SelectedItem = configuration.WallpaperFit;
        editor.Background = new SolidColorBrush(wallpaperColor);
        UpdateEditorContrast();
        RefreshPlaceholderList();
    }

    private void AddEnvironment()
    {
        var items = Environment.GetEnvironmentVariables().Keys.Cast<object>().Select(k => k.ToString()!).Order().Select(k => (k, k, "Environment", k));
        AddFromPicker("Environment variables", items);
    }

    private void AddNetworking()
    {
        var dialog = new NetworkSourceWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;
        foreach (var definition in dialog.Selected) AddDefinition(definition);
    }

    private void AddAzure() => AddFromPicker("Azure metadata", new[]
    {
        ("AzureVmName", "Azure VM name", "Azure", "name"), ("AzureVmSize", "Azure VM size", "Azure", "vmSize"), ("AzureRegion", "Azure region", "Azure", "location")
    });

    private void AddCim() => AddFromPicker("CIM system information", new[]
    {
        ("OSName", "Operating system", "CIM", "OSName"), ("OSVersion", "Windows release", "CIM", "OSVersion"),
        ("Manufacturer", "System manufacturer", "CIM", "Manufacturer"), ("Model", "System model", "CIM", "Model"),
        ("SerialNumber", "System serial number", "CIM", "SerialNumber"), ("Processor", "Processor", "CIM", "Processor")
    });

    private void AddRegistry()
    {
        var dialog = new RegistrySourceWindow { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Definition is { } definition) AddDefinition(definition);
    }

    private void AddUserData()
    {
        var open = new OpenFileDialog { Title = "Choose UserData JSON", Filter = "JSON or encoded data|*.json;*.bin;*.txt|All files|*.*" };
        if (open.ShowDialog(this) != true) return;
        try
        {
            var content = DataSources.ReadTextAtMost(open.FileName, InputLimits.MaxSourceCharacters)
                ?? throw new IOException($"The file exceeds the {InputLimits.MaxUserDataBytes / 1024} KiB decoded-data limit or could not be read.");
            var fields = UserDataJson.Parse(content);
            AddFromPicker("UserData values", fields.Select(f => (MakeName("UserData" + f.Key), f.Label, "UserData", open.FileName + "|" + f.Key["userdata:".Length..])));
        }
        catch (Exception e) { status.Text = "UserData could not be read: " + e.Message; }
    }

    private void AddFromPicker(string title, IEnumerable<(string Name, string Label, string Kind, string Argument)> items)
    {
        var dialog = new SourcePickerWindow(title, items.ToArray()) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        foreach (var item in dialog.Selected) AddDefinition(new PlaceholderDefinition(MakeName(item.Name), item.Label, item.Kind, item.Argument));
    }

    private void AddDefinition(PlaceholderDefinition definition)
    {
        var existing = definitions.FirstOrDefault(d => d.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            if (definitions.Count >= AppLimits.MaxPlaceholders)
            {
                status.Text = $"No more than {AppLimits.MaxPlaceholders} placeholders can be configured.";
                return;
            }
            definitions.Add(definition);
        }
        else definition = existing;
        RefreshPlaceholderList(); InsertToken(definition.Name);
    }

    private void InsertToken(string name) { editor.CaretPosition.InsertTextInRun("{" + name + "}"); editor.Focus(); }
    private void RefreshPlaceholderList() { placeholders.ItemsSource = null; placeholders.ItemsSource = definitions; placeholders.DisplayMemberPath = "Name"; }
    internal static string MakeName(string value) { var name = Regex.Replace(value, "[^A-Za-z0-9_]", "_").Trim('_'); return string.IsNullOrEmpty(name) ? "Value" : name; }
}
