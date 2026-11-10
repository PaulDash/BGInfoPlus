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
    private readonly CheckBox runAtSignIn = new() { Content = "Always apply at login", Foreground = UITheme.TextBrush, IsChecked = false, Margin = new Thickness(8, 8, 0, 0) };
    private readonly ComboBox resolutions = new() { Width = 170, Height = 34, Margin = new Thickness(0, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 4, 0) };
    private readonly List<DisplayResolution> availableResolutions = [];
    private readonly List<PlaceholderDefinition> definitions = [];
    private ConfigurationScope loadedScope = ConfigurationScope.BuiltIn;
    private readonly List<Color> recentColors = [];
    private readonly StackPanel recentFontColors = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel recentWallpaperColors = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private ComboBox? fontSizeControl;
    private double currentFontSize = 24;
    private Color wallpaperColor = Color.FromRgb(15, 23, 42);
    private readonly Border wallpaperColorSwatch = new() { Width = 15, Height = 15, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1) };
    private readonly Border fontColorSwatch = new() { Width = 15, Height = 4, Background = Brushes.White, BorderBrush = Brushes.Black, BorderThickness = new Thickness(0.5) };

    public Editor()
    {
        var availableHeight = Math.Max(620, SystemParameters.WorkArea.Height - 48);
        Title = "BGInfoPlus"; Width = 1180; Height = Math.Min(900, availableHeight); MinWidth = 900; MinHeight = 620;
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
        AddToolbarButton(bar, FontColorIcon(), "Font color", ChooseFontColor);
        bar.Children.Add(recentFontColors);
        AddSeparator(bar);
        wallpaperColorSwatch.Background = new SolidColorBrush(wallpaperColor);
        AddToolbarButton(bar, wallpaperColorSwatch, "Wallpaper color", ChooseWallpaperColor);
        bar.Children.Add(recentWallpaperColors);
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

    private void AddRecentColor(Color color)
    {
        AddRecentColorToHistory(recentColors, color);
        RefreshRecentColorButtons();
    }

    internal static void AddRecentColorToHistory(List<Color> colors, Color color)
    {
        colors.Remove(color);
        colors.Insert(0, color);
        if (colors.Count > 6) colors.RemoveRange(6, colors.Count - 6);
    }

    private void RefreshRecentColorButtons()
    {
        recentFontColors.Children.Clear();
        recentWallpaperColors.Children.Clear();
        foreach (var color in recentColors)
        {
            recentFontColors.Children.Add(RecentColorButton(color, "font color", ApplyFontColor));
            recentWallpaperColors.Children.Add(RecentColorButton(color, "wallpaper color", ApplyWallpaperColor));
        }
    }

    private static Button RecentColorButton(Color color, string target, Action<Color> apply)
    {
        var button = new Button
        {
            Content = new Border
            {
                Width = 15, Height = 15, Background = new SolidColorBrush(color),
                BorderBrush = Brushes.Black, BorderThickness = new Thickness(1)
            },
            Width = 24, Height = 24, Padding = new Thickness(2), Margin = new Thickness(0, 0, 2, 0),
            ToolTip = $"Apply {ColorText(color)} as {target}"
        };
        button.Click += (_, _) => apply(color);
        return button;
    }

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

    private UIElement FontColorIcon()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = "A", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 14 });
        panel.Children.Add(fontColorSwatch); return panel;
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
        var bar = new Grid { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        for (var i = 0; i < 5; i++) bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        resolutions.ToolTip = "Wallpaper output resolution";
        resolutions.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(resolutions, 0); bar.Children.Add(resolutions);
        var preview = new Button { Content = "Preview", Height = 34, MinWidth = 88, Margin = new Thickness(8, 0, 0, 0) }; preview.Click += async (_, _) => await PreviewAsync();
        Grid.SetColumn(preview, 1); bar.Children.Add(preview);
        var apply = new Button { Content = "Apply wallpaper", Height = 34, MinWidth = 128, Margin = new Thickness(8, 0, 0, 0), Style = UITheme.AccentButtonStyle() }; apply.Click += async (_, _) => await ApplyAsync();
        Grid.SetColumn(apply, 2); bar.Children.Add(apply);
        var export = new Button { Content = "Export image…", Height = 34, MinWidth = 112, Margin = new Thickness(8, 0, 0, 0) }; export.Click += async (_, _) => await ExportAsync();
        Grid.SetColumn(export, 3); bar.Children.Add(export);
        var deploy = new Button { Content = "Deploy for all users…", Height = 34, MinWidth = 156, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Save this design as the machine-wide template and apply it whenever any user signs in" };
        deploy.Click += async (_, _) => await DeployForAllUsersAsync(); Grid.SetColumn(deploy, 4); bar.Children.Add(deploy);
        Grid.SetColumn(runAtSignIn, 2); Grid.SetRow(runAtSignIn, 1); bar.Children.Add(runAtSignIn);
        var exportConfiguration = new Button { Content = "Export config…", Height = 30, MinWidth = 112, Margin = new Thickness(8, 6, 0, 0), ToolTip = "Export this editable layout and its placeholder mappings as a JSON configuration" };
        exportConfiguration.Click += (_, _) => ExportConfiguration(); Grid.SetColumn(exportConfiguration, 3); Grid.SetRow(exportConfiguration, 1); bar.Children.Add(exportConfiguration);
        var deployDefault = new Button { Content = "Set as default…", Height = 30, MinWidth = 156, Margin = new Thickness(8, 6, 0, 0), ToolTip = "Use this layout for users who do not have a personal configuration" };
        deployDefault.Click += async (_, _) => await DeployMachineConfigurationAsync(ConfigurationScope.Default); Grid.SetColumn(deployDefault, 4); Grid.SetRow(deployDefault, 1); bar.Children.Add(deployDefault);
        return bar;
    }

    private AppConfiguration CurrentConfiguration()
    {
        var selected = resolutions.SelectedItem as DisplayResolution ?? ScreenResolution.Current;
        var configuration = new AppConfiguration(DocumentTools.Save(editor.Document), definitions.ToArray(), runAtSignIn.IsEnabled && runAtSignIn.IsChecked == true, selected.Width, selected.Height, ColorText(wallpaperColor));
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

    private void ChooseFontColor()
    {
        var current = editor.Selection.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush;
        var selected = ShowColorDialog(current?.Color ?? Colors.White);
        if (selected is not Color color) return;
        AddRecentColor(color);
        ApplyFontColor(color);
    }

    private void ChooseWallpaperColor()
    {
        var selected = ShowColorDialog(wallpaperColor);
        if (selected is not Color color) return;
        AddRecentColor(color);
        ApplyWallpaperColor(color);
    }

    private void ApplyFontColor(Color color)
    {
        var brush = new SolidColorBrush(color);
        editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
        fontColorSwatch.Background = brush;
        editor.Focus();
    }

    private void ApplyWallpaperColor(Color color)
    {
        wallpaperColor = color;
        var brush = new SolidColorBrush(wallpaperColor);
        wallpaperColorSwatch.Background = brush; editor.Background = brush; UpdateEditorContrast();
    }

    private Color? ShowColorDialog(Color initial)
    {
        return NativeColorDialog.Show(new WindowInteropHelper(this).Handle, initial);
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

    private async Task ApplyAsync()
    {
        try
        {
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

    private Task DeployForAllUsersAsync() => DeployMachineConfigurationAsync(ConfigurationScope.AllUsers);

    private async Task DeployMachineConfigurationAsync(ConfigurationScope targetScope)
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
            start.ArgumentList.Add("-Scope"); start.ArgumentList.Add(targetScope == ConfigurationScope.Default ? "Default" : "AllUsers");
            start.ArgumentList.Add("-TemplatePath"); start.ArgumentList.Add(stagingPath);
            start.ArgumentList.Add("-Force");
            start.ArgumentList.Add("-PauseOnError");
            status.Text = $"Waiting for the elevated {(targetScope == ConfigurationScope.Default ? "default" : "all-users")} deployment…";
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The deployment helper could not be started.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException($"The deployment helper exited with code {process.ExitCode}. See its PowerShell window for details.");
            runAtSignIn.IsChecked = true; runAtSignIn.IsEnabled = false;
            runAtSignIn.Content = "Applied at login by machine task";
            loadedScope = File.Exists(ConfigurationStore.AllUsersPath) ? ConfigurationScope.AllUsers : targetScope;
            status.Text = targetScope == ConfigurationScope.Default && loadedScope == ConfigurationScope.AllUsers
                ? "Default configuration saved, but the existing all-users configuration still takes precedence."
                : targetScope == ConfigurationScope.Default
                    ? "Default configuration deployed. Users without a personal configuration will receive it at logon."
                    : "All-users configuration deployed. BGInfoPlus will enforce it at every user logon.";
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
            loadedScope = resolved.Scope;
            var configuration = resolved.Configuration;
            editor.Document = DocumentTools.Load(configuration.Document); definitions.AddRange(configuration.Placeholders);
            var machineLogon = resolved.Scope is ConfigurationScope.AllUsers or ConfigurationScope.Default;
            runAtSignIn.IsChecked = machineLogon || configuration.RunAtSignIn;
            runAtSignIn.IsEnabled = !machineLogon;
            runAtSignIn.Content = machineLogon ? "Applied at login by machine task" : "Always apply at login";
            runAtSignIn.ToolTip = machineLogon ? "The installed machine task applies the effective configuration for every interactive user." : null;
            wallpaperColor = ParseColor(configuration.BackgroundColor, wallpaperColor);
            var saved = availableResolutions.FirstOrDefault(r => r.Width == configuration.ImageWidth && r.Height == configuration.ImageHeight);
            if (saved is null) { saved = new DisplayResolution(configuration.ImageWidth, configuration.ImageHeight, "Saved"); availableResolutions.Add(saved); resolutions.Items.Refresh(); }
            resolutions.SelectedItem = saved;
            editor.Background = new SolidColorBrush(wallpaperColor); wallpaperColorSwatch.Background = new SolidColorBrush(wallpaperColor); UpdateEditorContrast();
            RefreshPlaceholderList();
            status.Text = resolved.Scope switch
            {
                ConfigurationScope.User => $"Loaded your configuration from {resolved.Path}.",
                ConfigurationScope.AllUsers => $"Loaded the enforced all-users configuration from {resolved.Path}.",
                ConfigurationScope.Default => $"Loaded the machine default because you do not yet have a personal configuration.",
                _ => "Using the built-in starter configuration."
            };
        }
        catch (Exception e) { editor.Document = DocumentTools.NewDocument(); status.Text = "Configuration could not be loaded: " + e.Message; }
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
