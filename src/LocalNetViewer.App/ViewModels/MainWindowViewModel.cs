using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNetViewer.Core.Networking;

namespace LocalNetViewer.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly INetworkDiscoveryService _discovery;
    private readonly IPortScanService _portScan;
    private readonly ICommandService _commands;
    private readonly ICaptureInventoryService _capture;
    private readonly IPlatformStatusService _platform;
    private CancellationTokenSource? _operationCts;
    private string _selectedSection = "探索";
    private string _status = "準備完了";
    private string _busyMessage = "";
    private bool _isBusy;
    private string _scanLog = "";
    private HostRecord? _selectedHost;
    private NetworkInterfaceInfo? _selectedInterface;
    private string _targetRange = "127.0.0.1/32";
    private CommandDefinition? _selectedCommand;
    private string _commandTarget = "127.0.0.1";
    private string _commandArguments = "";
    private string _commandOutput = "";
    private string _commandError = "";
    private string _commandSummary = "未実行";
    private string _addressInput = "";
    private int _prefixLength = 24;
    private string _portInput = "22,53,80,139,443,445,3389,8080";
    private int _portTimeoutMs = 500;
    private int _portParallelism = 64;

    public MainWindowViewModel(
        INetworkDiscoveryService discovery,
        IPortScanService portScan,
        ICommandService commands,
        ICaptureInventoryService capture,
        IPlatformStatusService platform)
    {
        _discovery = discovery;
        _portScan = portScan;
        _commands = commands;
        _capture = capture;
        _platform = platform;

        Sections = new ObservableCollection<string>(["探索", "詳細", "ポート", "コマンド", "IP計算", "キャプチャ", "設定"]);
        Interfaces = new ObservableCollection<NetworkInterfaceInfo>(_discovery.GetInterfaces());
        SelectedInterface = Interfaces.FirstOrDefault(IsPreferredLanInterface) ?? Interfaces.FirstOrDefault();
        Commands = new ObservableCollection<CommandDefinition>(_commands.GetDefinitions());
        SelectedCommand = Commands.FirstOrDefault(command => command.Id == "ping") ?? Commands.FirstOrDefault();
        Hosts = new ObservableCollection<HostRecord>();
        SelectedHost = null;
        PortResults = new ObservableCollection<PortScanResult>();
        CommandHistory = new ObservableCollection<CommandHistoryRecord>();
        Packets = new ObservableCollection<PacketRecord>(_capture.GetInitialPackets());
        Capabilities = new ObservableCollection<CapabilityStatus>(_platform.GetCapabilities());
        IpRows = new ObservableCollection<IpConversionRow>();
    }

    public ObservableCollection<string> Sections { get; }
    public ObservableCollection<NetworkInterfaceInfo> Interfaces { get; }
    public ObservableCollection<HostRecord> Hosts { get; }
    public ObservableCollection<PortScanResult> PortResults { get; }
    public ObservableCollection<CommandDefinition> Commands { get; }
    public ObservableCollection<CommandHistoryRecord> CommandHistory { get; }
    public ObservableCollection<PacketRecord> Packets { get; }
    public ObservableCollection<CapabilityStatus> Capabilities { get; }
    public ObservableCollection<IpConversionRow> IpRows { get; }

    public string SelectedSection
    {
        get => _selectedSection;
        set => SetProperty(ref _selectedSection, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string BusyMessage
    {
        get => _busyMessage;
        private set => SetProperty(ref _busyMessage, value);
    }

    public string ScanLog
    {
        get => _scanLog;
        set => SetProperty(ref _scanLog, value);
    }

    public HostRecord? SelectedHost
    {
        get => _selectedHost;
        set
        {
            if (SetProperty(ref _selectedHost, value) && value is not null)
            {
                CommandTarget = value.IpAddress;
            }
        }
    }

    public NetworkInterfaceInfo? SelectedInterface
    {
        get => _selectedInterface;
        set
        {
            if (SetProperty(ref _selectedInterface, value) && value is not null)
            {
                TargetRange = value.Cidr;
            }
        }
    }

    public string TargetRange
    {
        get => _targetRange;
        set => SetProperty(ref _targetRange, value);
    }

    public CommandDefinition? SelectedCommand
    {
        get => _selectedCommand;
        set => SetProperty(ref _selectedCommand, value);
    }

    public string CommandTarget
    {
        get => _commandTarget;
        set => SetProperty(ref _commandTarget, value);
    }

    public string CommandArguments
    {
        get => _commandArguments;
        set => SetProperty(ref _commandArguments, value);
    }

    public string CommandOutput
    {
        get => _commandOutput;
        set => SetProperty(ref _commandOutput, value);
    }

    public string CommandError
    {
        get => _commandError;
        set => SetProperty(ref _commandError, value);
    }

    public string CommandSummary
    {
        get => _commandSummary;
        set => SetProperty(ref _commandSummary, value);
    }

    public string AddressInput
    {
        get => _addressInput;
        set => SetProperty(ref _addressInput, value);
    }

    public int PrefixLength
    {
        get => _prefixLength;
        set => SetProperty(ref _prefixLength, Math.Clamp(value, 0, 32));
    }

    public string PortInput
    {
        get => _portInput;
        set => SetProperty(ref _portInput, value);
    }

    public int PortTimeoutMs
    {
        get => _portTimeoutMs;
        set => SetProperty(ref _portTimeoutMs, Math.Clamp(value, 50, 10_000));
    }

    public int PortParallelism
    {
        get => _portParallelism;
        set => SetProperty(ref _portParallelism, Math.Clamp(value, 1, 256));
    }

    public string PlatformName => _platform.PlatformName;
    public string CaptureStatus => _capture.GetCaptureStatus().Status;
    public string CaptureReason => _capture.GetCaptureStatus().Reason;

    public async Task StartDiscoveryAsync()
    {
        _operationCts?.Cancel();
        _operationCts = new CancellationTokenSource();
        BeginBusy("LANを探索しています");
        Status = "探索中...";
        ScanLog = $"{DateTime.Now:HH:mm:ss} 探索を開始しました。";

        try
        {
            var cidr = string.IsNullOrWhiteSpace(TargetRange)
                ? SelectedInterface?.Cidr ?? "127.0.0.1/32"
                : TargetRange.Trim();
            var settings = new ScanSettings
            {
                InterfaceName = SelectedInterface?.Name ?? "自動",
                AddressOrCidr = cidr,
                IncludeArp = true,
                ResolveNames = true,
            };
            var results = await _discovery.DiscoverAsync(settings, _operationCts.Token).ConfigureAwait(true);
            Replace(Hosts, results);
            SelectedHost = Hosts.FirstOrDefault();
            var named = Hosts.Count(host => !string.IsNullOrWhiteSpace(host.HostName));
            var withMac = Hosts.Count(host => !string.IsNullOrWhiteSpace(host.MacAddress));
            var withVendor = Hosts.Count(host => !string.IsNullOrWhiteSpace(host.Vendor));
            Status = $"{Hosts.Count}件のホストを検出 / 名前 {named} / MAC {withMac} / ベンダー {withVendor}";
            ScanLog += $"{Environment.NewLine}{DateTime.Now:HH:mm:ss} 探索が完了しました。名前 {named}、MAC {withMac}、ベンダー {withVendor}。";
        }
        catch (OperationCanceledException)
        {
            Status = "探索を停止しました";
        }
        catch (Exception ex)
        {
            Status = "探索に失敗しました";
            ScanLog += $"{Environment.NewLine}{DateTime.Now:HH:mm:ss} {ex.Message}";
        }
        finally
        {
            EndBusy();
        }
    }

    public void StopOperation()
    {
        _operationCts?.Cancel();
        Status = "停止要求を送信しました";
        if (IsBusy)
        {
            BusyMessage = "停止しています";
        }
    }

    public async Task StartPortScanAsync()
    {
        _operationCts?.Cancel();
        _operationCts = new CancellationTokenSource();
        BeginBusy("TCPポートを確認しています");
        Status = "ポート確認中...";

        try
        {
            var hosts = Hosts.Select(host => host.IpAddress).Where(item => IPAddress.TryParse(item, out _)).Take(8).DefaultIfEmpty("127.0.0.1").ToArray();
            var ports = ParsePorts(PortInput);
            var results = await _portScan.ScanTcpAsync(hosts, ports, PortTimeoutMs, PortParallelism, _operationCts.Token).ConfigureAwait(true);
            Replace(PortResults, results);
            Status = $"{PortResults.Count}件のポート結果";
        }
        catch (OperationCanceledException)
        {
            Status = "ポート確認を停止しました";
        }
        catch (Exception ex)
        {
            Status = "ポート確認に失敗しました";
            CommandError = ex.Message;
        }
        finally
        {
            EndBusy();
        }
    }

    public async Task RunSelectedCommandAsync()
    {
        if (SelectedCommand is null)
        {
            return;
        }

        if (!SelectedCommand.IsAvailable)
        {
            CommandSummary = "利用不可";
            CommandError = SelectedCommand.UnavailableReason;
            Status = SelectedCommand.UnavailableReason;
            return;
        }

        _operationCts?.Cancel();
        _operationCts = new CancellationTokenSource();
        BeginBusy($"{SelectedCommand.DisplayName}を実行しています");
        Status = $"{SelectedCommand.DisplayName}を実行中...";

        try
        {
            var result = await _commands.RunAsync(SelectedCommand.Id, CommandTarget, CommandArguments, _operationCts.Token).ConfigureAwait(true);
            CommandOutput = string.IsNullOrWhiteSpace(result.Output) ? "(標準出力なし)" : result.Output;
            CommandError = result.Error;
            CommandSummary = $"exit {result.ExitCode} / {result.Elapsed.TotalMilliseconds:N0} ms / {result.CommandLine}";
            CommandHistory.Insert(0, new CommandHistoryRecord
            {
                Command = SelectedCommand.DisplayName,
                Target = CommandTarget,
                Status = result.ExitCode == 0 ? "成功" : "失敗",
                Elapsed = $"{result.Elapsed.TotalMilliseconds:N0} ms",
                ExecutedAt = result.StartedAt.ToString("HH:mm:ss"),
            });
            Status = result.ExitCode == 0 ? "コマンドが完了しました" : "コマンドがエラー終了しました";
        }
        catch (OperationCanceledException)
        {
            Status = "コマンドを停止しました";
        }
        catch (Exception ex)
        {
            CommandError = ex.Message;
            Status = "コマンド実行に失敗しました";
        }
        finally
        {
            EndBusy();
        }
    }

    public void RecalculateIp()
    {
        try
        {
            var result = IpAddressCalculator.CalculateIPv4(AddressInput, PrefixLength);
            Replace(IpRows, result.Rows);
            Status = $"IP計算: {result.Input}";
        }
        catch (Exception ex)
        {
            IpRows.Clear();
            Status = ex.Message;
        }
    }

    private static int[] ParsePorts(string text) =>
        text.Split([',', ' ', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.TryParse(item, out var port) ? port : -1)
            .Where(port => port is > 0 and <= 65535)
            .Distinct()
            .DefaultIfEmpty(80)
            .ToArray();

    private void BeginBusy(string message)
    {
        BusyMessage = message;
        IsBusy = true;
    }

    private void EndBusy()
    {
        IsBusy = false;
        BusyMessage = "";
    }

    private static bool IsPreferredLanInterface(NetworkInterfaceInfo item)
    {
        if (!IPAddress.TryParse(item.Address, out var address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }
}
