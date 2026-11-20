using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace SerialStressTester.Services;

public class SerialWorkerService : IDisposable
{
    private SerialPort? _txPort;
    private SerialPort? _rxPort;
    private CancellationTokenSource? _cts;
    private Task? _txTask;
    private Task? _rxTask;

    private volatile int _intervalMs = 100;

    public int IntervalMs
    {
        get => _intervalMs;
        set => _intervalMs = Math.Clamp(value, 1, 1000);
    }

    // Hændelser bruger nu byte (0-255) i stedet for ushort
    public event Action<byte>? OnValueSent;
    public event Action<byte>? OnOverrunValueReceived;
    public event Action<string>? OnError;

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

    public void Start(string txPortName, string rxPortName, int baudRate = 115200)
    {
        Stop();

        try
        {
            _txPort = new SerialPort(txPortName, baudRate, Parity.None, 8, StopBits.One)
            {
                WriteTimeout = 500
            };
            _txPort.Open();

            _rxPort = new SerialPort(rxPortName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 500
            };
            _rxPort.Open();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _txTask = Task.Factory.StartNew(() => TransmitLoop(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _rxTask = Task.Factory.StartNew(() => ReceiveLoop(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Stop();
            OnError?.Invoke($"Kunne ikke åbne porte: {ex.Message}");
        }
    }

    private void TransmitLoop(CancellationToken token)
    {
        byte currentValue = 0;
        byte[] buffer = new byte[1];
        var sw = Stopwatch.StartNew();
        long nextSendTick = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                long elapsedMs = sw.ElapsedMilliseconds;
                if (elapsedMs >= nextSendTick)
                {
                    buffer[0] = currentValue;

                    _txPort?.Write(buffer, 0, 1);
                    OnValueSent?.Invoke(currentValue);

                    // Tæl op og wrap automatisk fra 255 -> 0 via C# byte overflow
                    unchecked
                    {
                        currentValue++;
                    }

                    nextSendTick = elapsedMs + _intervalMs;
                }
                else
                {
                    long remaining = nextSendTick - elapsedMs;
                    if (remaining > 2)
                    {
                        Thread.Sleep(1);
                    }
                    else
                    {
                        Thread.SpinWait(50);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"TX Fejl: {ex.Message}");
                break;
            }
        }
    }

    private void ReceiveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_rxPort == null || !_rxPort.IsOpen) break;

                if (_rxPort.BytesToRead > 0)
                {
                    int b = _rxPort.ReadByte();
                    if (b != -1)
                    {
                        byte overrunValue = (byte)b;
                        OnOverrunValueReceived?.Invoke(overrunValue);
                    }
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
            catch (TimeoutException)
            {
                // Ignorer read timeouts
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"RX Fejl: {ex.Message}");
                break;
            }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();

        try
        {
            _txPort?.Close();
            _txPort?.Dispose();
            _rxPort?.Close();
            _rxPort?.Dispose();
        }
        catch { }

        _txPort = null;
        _rxPort = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}