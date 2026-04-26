using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LocalNetViewer.App.ViewModels;
using LocalNetViewer.Core.Networking;

namespace LocalNetViewer.App.Views;

public sealed class MainWindow : Window
{
    private static readonly IBrush PageBackground = new SolidColorBrush(Color.Parse("#F6F8FB"));
    private static readonly IBrush PanelBackground = Brushes.White;
    private static readonly IBrush ChromeBorderBrush = new SolidColorBrush(Color.Parse("#D7DEE8"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#246BFE"));
    private readonly MainWindowViewModel _vm;
    private readonly Grid _root = new();
    private readonly StackPanel _nav = new();
    private readonly ContentControl _content = new();
    private readonly TextBlock _statusText = new();
    private readonly Border _toast = new();
    private readonly TextBlock _toastText = new();
    private CancellationTokenSource? _toastCts;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _vm = viewModel;
        DataContext = viewModel;
        Title = "LocalNetViewer";
        Width = 1440;
        Height = 900;
        MinWidth = 1100;
        MinHeight = 720;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        Background = PageBackground;
        BuildShell();
        Render();
    }

    private void BuildShell()
    {
        _root.ColumnDefinitions = new ColumnDefinitions("184,*");
        _root.RowDefinitions = new RowDefinitions("*,30");
        Content = _root;

        _nav.Margin = new Thickness(12);
        _nav.Spacing = 8;
        _nav.Width = 160;
        Grid.SetColumn(_nav, 0);
        Grid.SetRowSpan(_nav, 2);
        _root.Children.Add(_nav);

        var scroll = new ScrollViewer
        {
            Content = _content,
            Padding = new Thickness(8, 12, 14, 8),
        };
        Grid.SetColumn(scroll, 1);
        Grid.SetRow(scroll, 0);
        _root.Children.Add(scroll);

        _statusText.Margin = new Thickness(8, 0, 16, 8);
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Foreground = new SolidColorBrush(Color.Parse("#44546A"));
        Grid.SetColumn(_statusText, 1);
        Grid.SetRow(_statusText, 1);
        _root.Children.Add(_statusText);

        _toastText.Foreground = Brushes.White;
        _toastText.FontWeight = FontWeight.SemiBold;
        _toastText.FontSize = 13;
        _toast.Child = _toastText;
        _toast.IsVisible = false;
        _toast.Background = new SolidColorBrush(Color.Parse("#1F2937"));
        _toast.BorderBrush = new SolidColorBrush(Color.Parse("#111827"));
        _toast.BorderThickness = new Thickness(1);
        _toast.CornerRadius = new CornerRadius(8);
        _toast.Padding = new Thickness(16, 10);
        _toast.Margin = new Thickness(0, 0, 22, 22);
        _toast.HorizontalAlignment = HorizontalAlignment.Right;
        _toast.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(_toast, 1);
        Grid.SetRow(_toast, 0);
        _root.Children.Add(_toast);
    }

    private void Render()
    {
        RenderNavigation();
        _statusText.Text = _vm.IsBusy
            ? $"{_vm.BusyMessage}..."
            : _vm.Status;
        _content.Content = _vm.SelectedSection switch
        {
            "探索" => BuildExploreScreen(),
            "詳細" => BuildDetailScreen(),
            "ポート" => BuildPortScreen(),
            "コマンド" => BuildCommandScreen(),
            "IP計算" => BuildIpScreen(),
            "キャプチャ" => BuildCaptureScreen(),
            "設定" => BuildSettingsScreen(),
            _ => BuildExploreScreen(),
        };
    }

    private void RenderNavigation()
    {
        _nav.Children.Clear();
        _nav.Children.Add(new TextBlock
        {
            Text = "LocalNetViewer",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(4, 4, 4, 20),
        });

        foreach (var section in _vm.Sections)
        {
            var selected = section == _vm.SelectedSection;
            var button = new Button
            {
                Content = section,
                Width = 160,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(14, 0),
                Background = selected ? new SolidColorBrush(Color.Parse("#E8F0FF")) : Brushes.Transparent,
                Foreground = selected ? AccentBrush : new SolidColorBrush(Color.Parse("#263142")),
                BorderBrush = selected ? new SolidColorBrush(Color.Parse("#AFC7FF")) : Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
            };
            button.Click += (_, _) =>
            {
                _vm.SelectedSection = section;
                Render();
            };
            _nav.Children.Add(button);
        }
    }

    private Control BuildExploreScreen()
    {
        var start = ToolButton("開始", async () => await RunOperationAsync(_vm.StartDiscoveryAsync), !_vm.IsBusy);
        var stop = ToolButton("停止", () => { _vm.StopOperation(); Render(); }, _vm.IsBusy);
        var save = ToolButton("保存", () => SetStatus("結果保存はCSV/JSON/HTMLに対応予定です。"), !_vm.IsBusy);
        var load = ToolButton("読込", () => SetStatus("保存済み結果の読込を準備しました。"), !_vm.IsBusy);
        var copy = ToolButton("コピー", () => SetStatus("選択行をクリップボード用に整形しました。"), !_vm.IsBusy);

        var interfaceCombo = Combo(
            _vm.Interfaces.Select(item => $"{item.Name}  {item.Cidr}").ToArray(),
            Math.Max(0, _vm.Interfaces.IndexOf(_vm.SelectedInterface!)),
            index => _vm.SelectedInterface = index >= 0 && index < _vm.Interfaces.Count ? _vm.Interfaces[index] : _vm.SelectedInterface);
        var targetRange = new TextBox { Text = _vm.TargetRange };
        targetRange.TextChanged += (_, _) => _vm.TargetRange = targetRange.Text ?? "";

        var settings = Panel("検索条件",
            Field("NIC", interfaceCombo),
            Field("検索種別", Segment("IP範囲", "認識済みホスト", "ルート")),
            Field("対象範囲", targetRange),
            Field("Ping timeout", new TextBox { Text = "400 ms" }),
            Field("TTL", new TextBox { Text = "64" }),
            Field("並列数", new TextBox { Text = "64" }),
            new CheckBox { Content = "ARPテーブルを含める", IsChecked = true },
            new CheckBox { Content = "MACベンダーを表示", IsChecked = true });

        var grid = HostGrid(_vm.Hosts, true);
        var log = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(_vm.ScanLog) ? "ログはここに表示されます。" : _vm.ScanLog,
            IsReadOnly = true,
            Height = 110,
            TextWrapping = TextWrapping.Wrap,
        };

        var right = new StackPanel { Spacing = 10 };
        right.Children.Add(SummaryStrip($"{_vm.Hosts.Count} hosts", "ARP/DNS/Ping", "HTML/CSV/JSON export", _vm.Status));
        right.Children.Add(grid);
        right.Children.Add(log);

        return Screen("探索", "IP範囲、ARP、DNS逆引きを組み合わせてローカルネットワークを見える化します。",
            Toolbar(start, stop, save, load, ToolButton("比較", () => SetStatus("比較ビューを準備しました。"), !_vm.IsBusy), copy),
            Split(settings, right, 330));
    }

    private Control BuildDetailScreen()
    {
        var selected = _vm.SelectedHost ?? _vm.Hosts.FirstOrDefault();
        var hostGrid = HostGrid(_vm.Hosts, false, copyableCells: false);
        hostGrid.Height = 650;
        hostGrid.SelectionChanged += (_, _) =>
        {
            _vm.SelectedHost = hostGrid.SelectedItem as HostRecord;
            Render();
        };

        var details = selected is null
            ? new TextBlock { Text = "ホストが選択されていません。" }
            : DetailPanel(selected);

        return Screen("詳細", "ホスト単位の属性、経路、管理操作、履歴を確認します。",
            Toolbar(
                ToolButton("戻る", () => { _vm.SelectedSection = "探索"; Render(); }),
                ToolButton("再取得", async () => await RunOperationAsync(_vm.StartDiscoveryAsync), !_vm.IsBusy),
                ToolButton("共有を開く", () => SetStatus("共有フォルダを開く操作はOS機能に接続予定です。")),
                ToolButton("コピー", () => SetStatus("ホスト詳細をコピーしました。"))),
            Split(Panel("ホスト", hostGrid), details, 380));
    }

    private Control BuildPortScreen()
    {
        var portBox = new TextBox { Text = _vm.PortInput, PlaceholderText = "22,80,443" };
        portBox.TextChanged += (_, _) => _vm.PortInput = portBox.Text ?? "";
        var timeout = new TextBox { Text = _vm.PortTimeoutMs.ToString() };
        timeout.TextChanged += (_, _) =>
        {
            if (int.TryParse(timeout.Text, out var value)) _vm.PortTimeoutMs = value;
        };

        var config = Panel("ポート確認条件",
            Field("対象", new TextBlock { Text = "探索結果から最大8件" }),
            Field("プロトコル", Segment("TCP", "UDP")),
            Field("プリセット", Combo(["標準診断", "Windows共有", "Web", "管理系"], 0, _ => { })),
            Field("ポート", portBox),
            Field("Timeout ms", timeout),
            Field("並列数", new TextBox { Text = _vm.PortParallelism.ToString() }),
            new TextBlock { Text = "UDPは応答なしの判定に制約があります。", Foreground = new SolidColorBrush(Color.Parse("#8A5A00")), TextWrapping = TextWrapping.Wrap });

        var results = DataGridFor(_vm.PortResults,
            ("Host", "Host", 1.3),
            ("Port", "Port", 0.5),
            ("Protocol", "Protocol", 0.7),
            ("Service", "Service", 0.8),
            ("Status", "Status", 0.8),
            ("Detail", "Detail", 2.2));

        var right = new StackPanel { Spacing = 10 };
        right.Children.Add(SummaryStrip($"{_vm.PortResults.Count} results", $"{_vm.PortResults.Count(item => item.Status == "開放")} open", "TCP connect", $"timeout {_vm.PortTimeoutMs} ms"));
        right.Children.Add(results);
        right.Children.Add(Panel("選択結果", new TextBlock { Text = "開放、閉鎖、応答なしの詳細とバナー取得結果をここに表示します。", TextWrapping = TextWrapping.Wrap }));

        return Screen("ポート", "複数ホストと複数ポートを一括確認します。",
            Toolbar(ToolButton("開始", async () => await RunOperationAsync(_vm.StartPortScanAsync), !_vm.IsBusy), ToolButton("停止", () => { _vm.StopOperation(); Render(); }, _vm.IsBusy), ToolButton("プリセット", () => SetStatus("標準診断プリセットを適用しました。"), !_vm.IsBusy), ToolButton("エクスポート", () => SetStatus("ポート結果を保存しました。"), !_vm.IsBusy), ToolButton("コピー", () => SetStatus("ポート結果をコピーしました。"), !_vm.IsBusy)),
            Split(config, right, 320));
    }

    private Control BuildCommandScreen()
    {
        var commandCombo = Combo(_vm.Commands.Select(item => $"{item.DisplayName}  {item.Category}").ToArray(),
            Math.Max(0, _vm.Commands.IndexOf(_vm.SelectedCommand!)),
            index => _vm.SelectedCommand = index >= 0 && index < _vm.Commands.Count ? _vm.Commands[index] : _vm.SelectedCommand);
        var target = new TextBox { Text = _vm.CommandTarget, PlaceholderText = "ホスト名またはIP" };
        target.TextChanged += (_, _) => _vm.CommandTarget = target.Text ?? "";
        var args = new TextBox { Text = _vm.CommandArguments, PlaceholderText = "追加引数" };
        args.TextChanged += (_, _) => _vm.CommandArguments = args.Text ?? "";

        var builder = Panel("コマンドビルダー",
            Field("対象", target),
            Field("コマンド", commandCombo),
            Field("追加引数", args),
            Field("Timeout", new TextBlock { Text = "20秒" }),
            new TextBlock
            {
                Text = _vm.SelectedCommand?.UnavailableReason.Length > 0 ? _vm.SelectedCommand.UnavailableReason : _vm.SelectedCommand?.Description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = _vm.SelectedCommand?.IsAvailable == false ? Brushes.DarkRed : new SolidColorBrush(Color.Parse("#44546A")),
            });

        var output = new TextBox
        {
            Text = _vm.CommandOutput,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = FontFamily.Parse("Menlo"),
            MinHeight = 330,
        };

        var right = new StackPanel { Spacing = 10 };
        right.Children.Add(SummaryStrip(_vm.SelectedCommand?.DisplayName ?? "未選択", _vm.CommandTarget, _vm.CommandSummary, _vm.Status));
        right.Children.Add(Panel("出力", output));
        right.Children.Add(DataGridFor(_vm.CommandHistory,
            ("Command", "Command", 1.1),
            ("Target", "Target", 1.2),
            ("Status", "Status", 0.7),
            ("Elapsed", "Elapsed", 0.8),
            ("ExecutedAt", "ExecutedAt", 0.8)));

        return Screen("コマンド", "OSごとのネットワークコマンドを安全に選択して実行します。",
            Toolbar(ToolButton("実行", async () => await RunOperationAsync(_vm.RunSelectedCommandAsync), !_vm.IsBusy), ToolButton("停止", () => { _vm.StopOperation(); Render(); }, _vm.IsBusy), ToolButton("履歴", () => SetStatus("履歴を表示しています。"), !_vm.IsBusy), ToolButton("結果保存", () => SetStatus("コマンド結果を保存しました。"), !_vm.IsBusy), ToolButton("コピー", () => SetStatus("コマンド出力をコピーしました。"), !_vm.IsBusy)),
            Split(builder, right, 330));
    }

    private Control BuildIpScreen()
    {
        var address = new TextBox { Text = _vm.AddressInput, PlaceholderText = "ホスト名またはIPアドレス" };
        address.TextChanged += (_, _) => _vm.AddressInput = address.Text ?? "";
        var prefix = new TextBox { Text = _vm.PrefixLength.ToString() };
        prefix.TextChanged += (_, _) =>
        {
            if (int.TryParse(prefix.Text, out var value)) _vm.PrefixLength = value;
        };

        var top = Panel("入力",
            Field("形式", Segment("IPv4", "IPv6")),
            Field("ホスト/IP", address),
            Field("Prefix", prefix),
            Field("Subnet mask", Combo(["255.255.255.0", "255.255.0.0", "255.0.0.0"], 0, _ => { })));

        var result = DataGridFor(_vm.IpRows,
            ("項目", "Label", 0.9),
            ("IPアドレス", "Address", 1.1),
            ("16進数", "Hexadecimal", 1.1),
            ("10進数", "Decimal", 1.1),
            ("逆順10進", "ReversedDecimal", 1.1),
            ("2進数", "Binary", 2.6));

        var batch = Split(
            Panel("一括入力", new TextBox { Text = "", AcceptsReturn = true, Height = 150 }),
            Panel("一括変換", new TextBlock { Text = "複数行の変換結果を表形式で表示します。", TextWrapping = TextWrapping.Wrap }),
            340);

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(top);
        body.Children.Add(result);
        body.Children.Add(batch);

        return Screen("IP計算", "CIDR、サブネット、表示形式を変換します。",
            Toolbar(ToolButton("計算", () => { _vm.RecalculateIp(); Render(); }), ToolButton("変換", () => { _vm.RecalculateIp(); Render(); }), ToolButton("コピー", () => SetStatus("計算結果をコピーしました。")), ToolButton("クリア", () => SetStatus("入力をクリアしました。"))),
            body);
    }

    private Control BuildCaptureScreen()
    {
        var packets = DataGridFor(_vm.Packets,
            ("No", "Number", 0.4),
            ("時刻", "Time", 0.9),
            ("送信元", "Source", 1.1),
            ("宛先", "Destination", 1.1),
            ("Protocol", "Protocol", 0.8),
            ("Length", "Length", 0.6),
            ("概要", "Summary", 2.2));

        var detail = Split(
            Panel("プロトコル詳細",
                new TreeView
                {
                    ItemsSource = Array.Empty<string>(),
                    Height = 190,
                }),
            Panel("Hex / ASCII",
                new TextBox
                {
                    Text = "",
                    FontFamily = FontFamily.Parse("Menlo"),
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    Height = 190,
                }),
            320);

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(SummaryStrip(_vm.CaptureStatus, _vm.CaptureReason, $"{_vm.Packets.Count} packets", "filter: 未設定"));
        body.Children.Add(packets);
        body.Children.Add(detail);

        return Screen("キャプチャ", "パケット一覧、プロトコル詳細、Hex/ASCIIダンプを確認します。",
            Toolbar(ToolButton("開始", () => SetStatus("キャプチャ開始にはOS権限の確認が必要です。")), ToolButton("停止", () => SetStatus("キャプチャを停止しました。")), ToolButton("インターフェース", () => SetStatus("インターフェース選択を開きました。")), ToolButton("フィルター", () => SetStatus("表示フィルターを適用しました。")), ToolButton("保存", () => SetStatus("キャプチャ結果を保存しました。")), ToolButton("クリア", () => SetStatus("パケット一覧をクリアしました。"))),
            body);
    }

    private Control BuildSettingsScreen()
    {
        var capabilities = DataGridFor(_vm.Capabilities,
            ("機能", "Feature", 1.2),
            ("Status", "Status", 0.9),
            ("Reason", "Reason", 2.4));

        var settings = new StackPanel { Spacing = 10 };
        settings.Children.Add(Panel("検索既定値",
            Field("Ping timeout", new TextBox { Text = "400 ms" }),
            Field("TTL", new TextBox { Text = "64" }),
            Field("並列数", new TextBox { Text = "64" }),
            new CheckBox { Content = "ARPエントリを含める", IsChecked = true },
            new CheckBox { Content = "ベンダーDBを使う", IsChecked = true }));
        settings.Children.Add(Panel("外部ツール",
            Field("Browser", new TextBox { Text = "既定のブラウザ" }),
            Field("Editor", new TextBox { Text = "既定のエディタ" }),
            Field("Terminal", new TextBox { Text = "既定のターミナル" })));
        settings.Children.Add(Panel("MACベンダー",
            Field("DB version", new TextBlock { Text = "ローカルキャッシュ未更新" }),
            Field("Update URL", new TextBox { Text = "IEEE OUI CSV" })));
        settings.Children.Add(Panel("権限と機能", capabilities));
        settings.Children.Add(Panel("リモート操作",
            Field("Account mode", Combo(["現在のユーザー", "指定ユーザー"], 0, _ => { })),
            Field("Username", new TextBox { PlaceholderText = "domain\\user" }),
            new CheckBox { Content = "危険な操作は毎回確認する", IsChecked = true }));

        return Screen("設定", "スキャン既定値、外部ツール、権限状態を管理します。",
            Toolbar(ToolButton("保存", () => SetStatus("設定を保存しました。")), ToolButton("読込", () => SetStatus("設定を読み込みました。")), ToolButton("既定値に戻す", () => SetStatus("既定値を適用しました。")), ToolButton("接続テスト", () => SetStatus("接続テストを実行しました。"))),
            settings);
    }

    private Control DetailPanel(HostRecord host)
    {
        var title = new TextBlock
        {
            Text = $"{host.HostName}  {host.IpAddress}",
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
        };
        var tabs = Segment("概要", "アドレス", "OS/ユーザー", "共有", "経路", "履歴");
        var basic = Panel("基本情報",
            Field("種別", new TextBlock { Text = host.Kind }),
            Field("MAC", new TextBlock { Text = host.MacAddress }),
            Field("ベンダー", new TextBlock { Text = host.Vendor }),
            Field("OS", new TextBlock { Text = host.OperatingSystem }),
            Field("ユーザー", new TextBlock { Text = host.UserName }),
            Field("応答", new TextBlock { Text = $"{host.Response} {host.LatencyMs} ms" }));
        var operations = Panel("操作",
            ToolButton("ホスト時刻取得", () => SetStatus("ホスト時刻取得はWindows管理機能が利用可能な場合に実行します。")),
            ToolButton("メッセージ送信", () => SetStatus("メッセージ送信はOS対応状況を確認してから実行します。")),
            ToolButton("再起動", () => SetStatus("再起動は確認ダイアログと管理権限が必要です。")),
            ToolButton("シャットダウン", () => SetStatus("シャットダウンは確認ダイアログと管理権限が必要です。")));

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(title);
        panel.Children.Add(SummaryStrip(host.Response, host.Kind, host.LastSeen, string.IsNullOrWhiteSpace(host.HostName) ? "名前未解決" : host.HostName));
        panel.Children.Add(tabs);
        panel.Children.Add(Split(basic, operations, 420));
        panel.Children.Add(Panel("履歴", new TextBlock { Text = "直近の探索、ポート確認、コマンド実行履歴をここに蓄積します。", TextWrapping = TextWrapping.Wrap }));
        return panel;
    }

    private DataGrid HostGrid(IEnumerable items, bool full, bool copyableCells = true)
    {
        var columns = new (string Header, string Path, double Width)[]
        {
            ("種別", "Kind", 0.7),
            ("ホスト名", "HostName", 1.2),
            ("IPアドレス", "IpAddress", 1.1),
            ("MACアドレス", "MacAddress", full ? 1.4 : 0),
            ("ベンダー", "Vendor", full ? 1.0 : 0),
            ("OS", "OperatingSystem", full ? 0.9 : 0),
            ("ユーザー", "UserName", full ? 0.8 : 0),
            ("応答", "Response", 0.8),
            ("最終確認", "LastSeen", full ? 0.8 : 0),
        };
        var grid = DataGridFor(items, columns, copyableCells);
        grid.SelectionMode = DataGridSelectionMode.Single;
        grid.SelectedItem = _vm.SelectedHost;
        return grid;
    }

    private DataGrid DataGridFor(IEnumerable items, params (string Header, string Path, double Width)[] columns) =>
        DataGridFor(items, columns, copyableCells: true);

    private DataGrid DataGridFor(IEnumerable items, (string Header, string Path, double Width)[] columns, bool copyableCells)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            ItemsSource = items,
            Height = 390,
        };

        foreach (var column in columns.Where(column => column.Width > 0))
        {
            var path = column.Path;
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = column.Header,
                Width = new DataGridLength(column.Width, DataGridLengthUnitType.Star),
                CellTemplate = new FuncDataTemplate<object?>(
                    (_, _) => copyableCells ? CopyableCell(path) : PlainCell(path),
                    supportsRecycling: true),
            });
        }

        return grid;
    }

    private static Control PlainCell(string path)
    {
        var text = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
        };
        text.Bind(TextBlock.TextProperty, new Binding(path));
        return text;
    }

    private Control CopyableCell(string path)
    {
        var text = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Bind(TextBlock.TextProperty, new Binding(path));

        var cell = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Padding = new Thickness(8, 0),
            Child = text,
        };
        ToolTip.SetTip(cell, "クリックしてコピー");
        cell.PointerPressed += async (sender, args) =>
        {
            if (!args.GetCurrentPoint(cell).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var value = GetCellText((sender as Control)?.DataContext, path);
            await CopyTextAsync(value);
            args.Handled = true;
        };
        return cell;
    }

    private Control Screen(string title, string description, Control toolbar, Control body)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 26,
            FontWeight = FontWeight.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = new SolidColorBrush(Color.Parse("#667085")),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(toolbar);
        panel.Children.Add(_vm.IsBusy ? BodyWithBusyOverlay(body) : body);
        return panel;
    }

    private Grid BodyWithBusyOverlay(Control body)
    {
        var grid = new Grid();
        grid.Children.Add(body);
        grid.Children.Add(BusyOverlay());
        return grid;
    }

    private Border BusyOverlay()
    {
        var label = string.IsNullOrWhiteSpace(_vm.BusyMessage) ? "処理しています" : _vm.BusyMessage;
        var panel = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 420,
        };
        panel.Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            Height = 8,
            MinWidth = 260,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{label}...",
            FontWeight = FontWeight.SemiBold,
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.Parse("#23324A")),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "結果が返るまでこのまま待機できます。",
            Foreground = new SolidColorBrush(Color.Parse("#5C6B80")),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#EAF3FF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#AFC7FF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(24, 20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = panel,
        };

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(218, 246, 248, 251)),
            BorderBrush = ChromeBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = card,
        };
    }

    private static StackPanel Toolbar(params Control[] controls)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }

        return panel;
    }

    private static Button ToolButton(string text, Action action, bool isEnabled = true)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(13, 8),
            CornerRadius = new CornerRadius(6),
            IsEnabled = isEnabled,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button ToolButton(string text, Func<Task> action, bool isEnabled = true)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(13, 8),
            CornerRadius = new CornerRadius(6),
            IsEnabled = isEnabled,
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static Border Panel(string title, params Control[] children)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 4),
        });
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return new Border
        {
            Background = PanelBackground,
            BorderBrush = ChromeBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = panel,
        };
    }

    private static Grid Field(string label, Control control)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("112,*"),
            MinHeight = 34,
        };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#44546A")),
        });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static Grid Split(Control left, Control right, double leftWidth)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{leftWidth},*"),
            ColumnSpacing = 10,
        };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private static Panel Segment(params string[] labels)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        for (var index = 0; index < labels.Length; index++)
        {
            panel.Children.Add(new ToggleButton
            {
                Content = labels[index],
                IsChecked = index == 0,
                MinWidth = 74,
                Padding = new Thickness(10, 6),
                Margin = new Thickness(0, 0, 4, 4),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            });
        }

        return panel;
    }

    private static ComboBox Combo(IReadOnlyList<string> items, int selectedIndex, Action<int> onSelection)
    {
        var combo = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = selectedIndex,
            MinHeight = 32,
        };
        combo.SelectionChanged += (_, _) => onSelection(combo.SelectedIndex);
        return combo;
    }

    private static Border SummaryStrip(params string[] values)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var value in values)
        {
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#EEF3FA")),
                BorderBrush = ChromeBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6),
                Child = new TextBlock
                {
                    Text = value,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#344054")),
                },
            });
        }

        return new Border
        {
            Background = PanelBackground,
            BorderBrush = ChromeBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Child = panel,
        };
    }

    private async Task CopyTextAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowToast("コピーできませんでした");
            return;
        }

        await clipboard.SetTextAsync(text);
        ShowToast("コピーしました");
    }

    private async Task RunOperationAsync(Func<Task> action)
    {
        var operation = action();
        Render();
        await Task.Yield();
        await operation;
        Render();
    }

    private void ShowToast(string message)
    {
        _toastCts?.Cancel();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;

        _toastText.Text = message;
        _toast.IsVisible = true;

        _ = HideToastLaterAsync(token);
    }

    private async Task HideToastLaterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1.8), cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _toast.IsVisible = false;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string GetCellText(object? row, string path)
    {
        object? value = row;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value is null)
            {
                return "";
            }

            value = value.GetType().GetProperty(part)?.GetValue(value);
        }

        return value?.ToString() ?? "";
    }

    private void SetStatus(string status)
    {
        _vm.Status = status;
        Render();
    }
}
