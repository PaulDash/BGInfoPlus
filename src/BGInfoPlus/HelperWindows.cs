using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Net.NetworkInformation;
using System.IO;
using Microsoft.Win32;

namespace BGInfoPlus;

internal sealed class SourcePickerWindow : Window
{
    private sealed record SourceOption(string Name, string Label, string Kind, string Argument);

    private readonly ListBox list = new()
    {
        SelectionMode = SelectionMode.Multiple,
        DisplayMemberPath = nameof(SourceOption.Label),
        Foreground = Brushes.Black,
        Background = Brushes.White
    };
    private readonly SourceOption[] items;
    public IReadOnlyList<(string Name, string Label, string Kind, string Argument)> Selected { get; private set; } = [];

    public SourcePickerWindow(string title, (string Name, string Label, string Kind, string Argument)[] items)
    {
        this.items = items.Select(i => new SourceOption(i.Name, i.Label, i.Kind, i.Argument)).ToArray();
        Title = title; Width = 520; Height = 540; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UITheme.Apply(this);
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(6) };
        search.TextChanged += (_, _) => list.ItemsSource = this.items.Where(i => i.Label.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || i.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase));
        DockPanel.SetDock(search, Dock.Top); root.Children.Add(search);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 6, 14, 6) };
        var add = new Button { Content = "Insert selected", IsDefault = true, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
        add.Click += (_, _) =>
        {
            Selected = list.SelectedItems.Cast<SourceOption>()
                .Select(i => (i.Name, i.Label, i.Kind, i.Argument)).ToArray();
            DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(add); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        list.ItemsSource = this.items; root.Children.Add(list);
    }
}

internal sealed class NetworkSourceWindow : Window
{
    private sealed record AdapterOption(NetworkInterface Adapter)
    {
        public string Label => $"{Adapter.Name}  —  {Adapter.NetworkInterfaceType}, {Adapter.OperationalStatus}";
    }
    private sealed record NetworkParameter(string Key, string Label);

    private readonly ComboBox adapters = new() { DisplayMemberPath = nameof(AdapterOption.Label), Height = 34 };
    private readonly ListBox parameters = new()
    {
        SelectionMode = SelectionMode.Multiple, DisplayMemberPath = nameof(NetworkParameter.Label),
        Foreground = Brushes.Black, Background = Brushes.White, MinHeight = 300
    };
    public IReadOnlyList<PlaceholderDefinition> Selected { get; private set; } = [];

    public NetworkSourceWindow()
    {
        Title = "Network interface data"; Width = 620; Height = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UITheme.Apply(this);
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(new TextBlock { Text = "Network interface", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
        top.Children.Add(adapters);
        top.Children.Add(new TextBlock { Text = "Select one or more values for this interface", Foreground = UITheme.SecondaryTextBrush, Margin = new Thickness(0, 12, 0, 5) });
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 6, 14, 6) });
        var insert = new Button { Content = "Insert selected", IsDefault = true, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
        insert.Click += (_, _) => Insert(); buttons.Children.Add(insert); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        parameters.ItemsSource = new[]
        {
            new NetworkParameter("IPv4", "IPv4 address"), new NetworkParameter("IPv6", "IPv6 address"),
            new NetworkParameter("Prefix", "Subnet prefix length"), new NetworkParameter("SubnetMask", "Full IPv4 subnet mask"),
            new NetworkParameter("Gateway", "Default gateway"), new NetworkParameter("DNS", "DNS servers"),
            new NetworkParameter("DhcpServer", "DHCP server"), new NetworkParameter("DhcpEnabled", "DHCP enabled"),
            new NetworkParameter("DnsSuffix", "DNS suffix"), new NetworkParameter("Wins", "WINS servers"),
            new NetworkParameter("MacAddress", "MAC address"), new NetworkParameter("Description", "Adapter description"),
            new NetworkParameter("Status", "Operational status"), new NetworkParameter("InterfaceType", "Interface type"),
            new NetworkParameter("Speed", "Link speed"), new NetworkParameter("Mtu", "MTU")
        };
        root.Children.Add(parameters);

        var choices = NetworkInterface.GetAllNetworkInterfaces().Select(n => new AdapterOption(n))
            .OrderByDescending(n => n.Adapter.OperationalStatus == OperationalStatus.Up).ThenBy(n => n.Adapter.Name).ToArray();
        adapters.ItemsSource = choices; if (choices.Length > 0) adapters.SelectedIndex = 0;
    }

    private void Insert()
    {
        if (adapters.SelectedItem is not AdapterOption adapter || parameters.SelectedItems.Count == 0) return;
        var adapterName = Editor.MakeName(adapter.Adapter.Name);
        Selected = parameters.SelectedItems.Cast<NetworkParameter>().Select(parameter => new PlaceholderDefinition(
            Editor.MakeName($"Network_{adapterName}_{parameter.Key}"), $"{adapter.Adapter.Name} — {parameter.Label}",
            "NetworkInterface", $"{adapter.Adapter.Id}|{parameter.Key}")).ToArray();
        DialogResult = true;
    }
}

internal sealed class RegistrySourceWindow : Window
{
    private sealed record RegistryValueItem(string Name, string DisplayName, string Type, string Data);

    private readonly ComboBox hive = new() { ItemsSource = new[] { "HKLM", "HKCU", "HKCR", "HKU" }, SelectedIndex = 0, Width = 110 };
    private readonly TreeView keys = new() { Background = Brushes.White, Foreground = Brushes.Black };
    private readonly ListView values = new() { Background = Brushes.White, Foreground = Brushes.Black };
    private readonly TextBox name = new() { MinWidth = 240 };
    private readonly TextBlock selectedPath = new() { Foreground = UITheme.SecondaryTextBrush, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock validation = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    public PlaceholderDefinition? Definition { get; private set; }

    public RegistrySourceWindow()
    {
        Title = "Browse registry values"; Width = 900; Height = 620; MinWidth = 720; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UITheme.Apply(this);
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var hiveRow = new StackPanel { Orientation = Orientation.Horizontal };
        hiveRow.Children.Add(new TextBlock { Text = "Hive", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.SemiBold });
        hiveRow.Children.Add(hive); top.Children.Add(hiveRow); top.Children.Add(selectedPath);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);

        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 6, 14, 6) });
        var add = new Button { Content = "Insert value", IsDefault = true, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
        add.Click += (_, _) => Insert(); buttons.Children.Add(add); DockPanel.SetDock(buttons, Dock.Right); bottom.Children.Add(buttons);
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock { Text = "Placeholder", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        nameRow.Children.Add(name); bottom.Children.Add(nameRow);
        var bottomArea = new StackPanel(); bottomArea.Children.Add(validation); bottomArea.Children.Add(bottom);
        DockPanel.SetDock(bottomArea, Dock.Bottom); root.Children.Add(bottomArea);

        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        columns.Children.Add(new Border { BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1), Child = keys });
        var valueView = new GridView();
        valueView.Columns.Add(new GridViewColumn { Header = "Name", DisplayMemberBinding = new System.Windows.Data.Binding(nameof(RegistryValueItem.DisplayName)), Width = 150 });
        valueView.Columns.Add(new GridViewColumn { Header = "Type", DisplayMemberBinding = new System.Windows.Data.Binding(nameof(RegistryValueItem.Type)), Width = 95 });
        valueView.Columns.Add(new GridViewColumn { Header = "Data", DisplayMemberBinding = new System.Windows.Data.Binding(nameof(RegistryValueItem.Data)), Width = 300 });
        values.View = valueView; values.SelectionChanged += (_, _) => UpdateSelection(); values.MouseDoubleClick += (_, _) => Insert();
        var valuesBorder = new Border { BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1), Child = values, Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(valuesBorder, 1); columns.Children.Add(valuesBorder); root.Children.Add(columns);

        keys.SelectedItemChanged += (_, _) => LoadValues();
        hive.SelectionChanged += (_, _) => LoadHive();
        Loaded += (_, _) => LoadHive();
    }

    private void LoadHive()
    {
        if (hive.SelectedItem is not string hiveName) return;
        keys.Items.Clear(); values.ItemsSource = null; validation.Text = "";
        var root = CreateKeyNode(hiveName, ""); keys.Items.Add(root); root.IsExpanded = true; root.IsSelected = true;
    }

    private TreeViewItem CreateKeyNode(string header, string path)
    {
        var node = new TreeViewItem { Header = header, Tag = path };
        node.Items.Add(new TreeViewItem { Header = "Loading…" });
        node.Expanded += (_, _) => Expand(node);
        return node;
    }

    private void Expand(TreeViewItem node)
    {
        if (node.Items.Count != 1 || node.Items[0] is not TreeViewItem first || first.Tag is not null) return;
        node.Items.Clear();
        try
        {
            using var key = OpenKey(node.Tag as string ?? "");
            if (key is null) return;
            foreach (var child in key.GetSubKeyNames().Order(StringComparer.CurrentCultureIgnoreCase))
            {
                var parentPath = node.Tag as string ?? "";
                node.Items.Add(CreateKeyNode(child, parentPath.Length == 0 ? child : parentPath + "\\" + child));
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { validation.Text = "Some registry keys cannot be opened: " + e.Message; }
    }

    private void LoadValues()
    {
        if (keys.SelectedItem is not TreeViewItem node) return;
        var path = node.Tag as string ?? ""; selectedPath.Text = $"{hive.SelectedItem}\\{path}".TrimEnd('\\'); validation.Text = "";
        try
        {
            using var key = OpenKey(path);
            values.ItemsSource = key?.GetValueNames().Order(StringComparer.CurrentCultureIgnoreCase).Select(valueName =>
            {
                object? data = null; string type = "Unknown";
                try { data = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames); type = key.GetValueKind(valueName).ToString(); } catch { }
                return new RegistryValueItem(valueName, valueName.Length == 0 ? "(Default)" : valueName, type, FormatValue(data));
            }).ToArray() ?? [];
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { values.ItemsSource = null; validation.Text = "This key cannot be opened: " + e.Message; }
    }

    private void UpdateSelection()
    {
        if (values.SelectedItem is not RegistryValueItem item || keys.SelectedItem is not TreeViewItem node) return;
        var path = node.Tag as string ?? ""; var leaf = path.Split('\\').LastOrDefault() ?? "Registry";
        name.Text = Editor.MakeName(item.Name.Length == 0 ? leaf + "_Default" : leaf + "_" + item.Name);
    }

    private void Insert()
    {
        if (values.SelectedItem is not RegistryValueItem item || keys.SelectedItem is not TreeViewItem node)
        { validation.Text = "Select a registry value first."; return; }
        var path = node.Tag as string ?? ""; var placeholder = Editor.MakeName(name.Text);
        if (placeholder.Length == 0) { validation.Text = "Enter a placeholder name."; return; }
        var hiveName = hive.SelectedItem?.ToString() ?? "HKLM";
        Definition = new PlaceholderDefinition(placeholder, $"{hiveName}\\{path} — {item.DisplayName}", "Registry", $"{hiveName}|{path}|{item.Name}");
        DialogResult = true;
    }

    private RegistryKey? OpenKey(string path)
    {
        var root = hive.SelectedItem?.ToString() switch
        {
            "HKCU" => Registry.CurrentUser, "HKCR" => Registry.ClassesRoot,
            "HKU" => Registry.Users, _ => Registry.LocalMachine
        };
        return root.OpenSubKey(path);
    }

    private static string FormatValue(object? value)
    {
        var text = value switch
        {
            null => "(value not set)", string[] strings => string.Join("; ", strings),
            byte[] bytes => string.Join("-", bytes.Select(b => b.ToString("X2"))), _ => value.ToString() ?? ""
        };
        return text.Length <= 240 ? text : text[..237] + "…";
    }
}

internal sealed class PreviewWindow : Window
{
    public PreviewWindow(ImageSource image)
    {
        Title = "Wallpaper preview"; Width = 1100; Height = 700; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black; Content = new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(12) };
    }
}
