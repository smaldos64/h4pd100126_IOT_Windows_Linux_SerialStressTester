using System;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SerialStressTester.Services;

namespace SerialStressTester.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly SerialWorkerService _serialService = new();
    private ushort _latestSentValue;
    private long _totalSentCount;
    private readonly DispatcherTimer _uiRefreshTimer;

    [ObservableProperty]
    private ObservableCollection<string> _availablePorts = new();

    [ObservableProperty]
    private string? _selectedTxPort;

    [ObservableProperty]
    private string? _selectedRxPort;

    [ObservableProperty]
    private int _selectedBaudRate = 115200;

    public int[] BaudRates { get; } = { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };

    [ObservableProperty]
    private int _intervalMs = 50;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Klar";

    [ObservableProperty]
    private ushort _displayLastSent;

    [ObservableProperty]
    private long _displayTotalSent;

    [ObservableProperty]
    private int _overrunCount;

    public ObservableCollection<string> OverrunLog { get; } = new();

    public MainViewModel()
    {
        RefreshPorts();

        _serialService.OnValueSent += val =>
        {
            _latestSentValue = val;
            System.Threading.Interlocked.Increment(ref _totalSentCount);
        };

        _serialService.OnOverrunValueReceived += overrunVal =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                OverrunCount++;
                string entry = $"[{DateTime.Now:HH:mm:ss.fff}] Overrun ved værdi: {overrunVal} (0x{overrunVal:X4})";
                OverrunLog.Insert(0, entry);

                // Begræns loggen i UI til de seneste 500 hændelser
                if (OverrunLog.Count > 500)
                {
                    OverrunLog.RemoveAt(OverrunLog.Count - 1);
                }
            });
        };

        _serialService.OnError += err =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText = err;
                Stop();
            });
        };

        // Opdater TX-tællere i UI 20 gange i sekundet (50ms) for at sikre flydende UI ved 1ms TX-rate
        _uiRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _uiRefreshTimer.Tick += (_, _) =>
        {
            DisplayLastSent = _latestSentValue;
            DisplayTotalSent = System.Threading.Interlocked.Read(ref _totalSentCount);
        };
    }

    partial void OnIntervalMsChanged(int value)
    {
        _serialService.IntervalMs = value;
    }

    [RelayCommand]
    private void RefreshPorts()
    {
        var ports = SerialPort.GetPortNames().OrderBy(p => p).ToList();
        AvailablePorts.Clear();
        foreach (var p in ports)
        {
            AvailablePorts.Add(p);
        }

        if (AvailablePorts.Count > 0 && string.IsNullOrEmpty(SelectedTxPort))
            SelectedTxPort = AvailablePorts[0];
        if (AvailablePorts.Count > 1 && string.IsNullOrEmpty(SelectedRxPort))
            SelectedRxPort = AvailablePorts[1];
    }

    [RelayCommand]
    private void Start()
    {
        if (string.IsNullOrWhiteSpace(SelectedTxPort) || string.IsNullOrWhiteSpace(SelectedRxPort))
        {
            StatusText = "Vælg venligst både en TX-port og en RX-port.";
            return;
        }

        if (SelectedTxPort == SelectedRxPort)
        {
            StatusText = "TX-port og RX-port må ikke være den samme.";
            return;
        }

        _totalSentCount = 0;
        _latestSentValue = 0;
        OverrunCount = 0;
        OverrunLog.Clear();

        _serialService.IntervalMs = IntervalMs;
        _serialService.Start(SelectedTxPort, SelectedRxPort, SelectedBaudRate);

        if (_serialService.IsRunning)
        {
            IsRunning = true;
            StatusText = $"Kører: Sender på {SelectedTxPort}, lytter på {SelectedRxPort} ({SelectedBaudRate} baud)";
            _uiRefreshTimer.Start();
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _uiRefreshTimer.Stop();
        _serialService.Stop();
        IsRunning = false;
        DisplayLastSent = _latestSentValue;
        DisplayTotalSent = System.Threading.Interlocked.Read(ref _totalSentCount);
        StatusText = "Stoppet";
    }

    public void Dispose()
    {
        _uiRefreshTimer.Stop();
        _serialService.Dispose();
    }
}