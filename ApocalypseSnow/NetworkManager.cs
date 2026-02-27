using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ApocalypseSnow;

public sealed class NetworkManager : IDisposable
{
    private readonly string _ip;
    private readonly int _port;

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;

    private CancellationTokenSource _cts = new();
    private Task? _recvTask;
    private Task? _reconnectTask;

    private long _lastSendLog;
    private long _lastRecvLog;

    private readonly object _sendLock = new();

    private static long NowMs() => Environment.TickCount64;

    private void Log1Hz(ref long last, string msg)
    {
        long now = NowMs();
        if (now - last >= 1000)
        {
            last = now;
            Debug.WriteLine(msg);
        }
    }

    public event Action<uint, float, float>? OnAuthState;                 // ack,x,y
    public event Action<uint, float, float>? OnJoinAck;                   // pid,sx,sy
    public event Action<float, float, StateList>? OnRemoteState;          // x,y,mask

    // RemoteShot: due int32 + charge int32.
    // NOTA: per il fix tiro, questi due int32 lato rete possono essere interpretati come dx/dy (offset) e non screen mouse assoluto.
    public event Action<int, int, int>? OnRemoteShot;                     // a,b,charge

    public bool IsConnected { get; private set; }
    public string? LastError { get; private set; }

    public NetworkManager(string ip, int port)
    {
        _ip = ip;
        _port = port;
    }

    public void StartAutoReconnect(TimeSpan retryEvery)
    {
        if (_reconnectTask != null) return;

        _reconnectTask = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                if (!IsConnected)
                    await ConnectOnceAsync(_cts.Token);

                try { await Task.Delay(retryEvery, _cts.Token); }
                catch (OperationCanceledException) { }
            }
        });
    }

    private async Task ConnectOnceAsync(CancellationToken ct)
    {
        try
        {
            if (IsConnected) return;

            CleanupSocket();

            var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(_ip, _port, ct);

            _tcpClient = client;
            _stream = client.GetStream();

            IsConnected = true;
            LastError = null;

            Debug.WriteLine("[NET] CONNECTED");

            _recvTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }
        catch (OperationCanceledException) { }
        catch (SocketException ex)
        {
            IsConnected = false;
            LastError = ex.Message;
            Debug.WriteLine($"[NET] connect failed: {ex.SocketErrorCode} {ex.Message}");
            CleanupSocket();
        }
        catch (Exception ex)
        {
            IsConnected = false;
            LastError = ex.Message;
            Debug.WriteLine($"[NET] connect failed: {ex.Message}");
            CleanupSocket();
        }
    }

    public void SendJoin(int a = 0, int b = 0)
    {
        if (!IsConnected || _stream == null || _tcpClient?.Connected != true)
            return;

        byte[] packet = new byte[9];
        packet[0] = (byte)MessageType.PlayerJoin;

        Buffer.BlockCopy(BitConverter.GetBytes(a), 0, packet, 1, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(b), 0, packet, 5, 4);

        try
        {
            lock (_sendLock) { _stream.Write(packet, 0, packet.Length); }
            Log1Hz(ref _lastSendLog, "[NET SEND] JOIN");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NET JOIN] error: {ex.Message}");
            IsConnected = false;
            CleanupSocket();
        }
    }

    public void SendStateTick(StateList mask, uint seq)
    {
        if (!IsConnected || _stream == null || _tcpClient?.Connected != true)
        {
            Log1Hz(ref _lastSendLog, "[NET SEND] offline (skip)");
            return;
        }

        byte[] packet = new byte[9];
        packet[0] = (byte)MessageType.State;

        Buffer.BlockCopy(BitConverter.GetBytes((int)mask), 0, packet, 1, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(seq), 0, packet, 5, 4);

        try
        {
            lock (_sendLock) { _stream.Write(packet, 0, packet.Length); }
            Log1Hz(ref _lastSendLog, $"[NET SEND] seq={seq} mask={(int)mask}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NET SEND] error: {ex.Message}");
            IsConnected = false;
            CleanupSocket();
        }
    }

    /// MsgShot 13B: [type][a:int32][b:int32][charge:int32]
    /// (a,b) = per ora sono i due int che decidiamo noi (mouse assoluto oppure dx/dy offset, ecc.)
    public void SendShot(ShotStruct shot)
    {
        if (!IsConnected || _stream == null || _tcpClient?.Connected != true)
            return;

        byte[] packet = new byte[13];
        packet[0] = (byte)MessageType.Shot;

        Buffer.BlockCopy(BitConverter.GetBytes(shot.mouseX), 0, packet, 1, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(shot.mouseY), 0, packet, 5, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(shot.charge), 0, packet, 9, 4);

        try
        {
            lock (_sendLock) { _stream.Write(packet, 0, packet.Length); }
            Log1Hz(ref _lastSendLog, $"[NET SEND] SHOT a={shot.mouseX} b={shot.mouseY} ch={shot.charge}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NET SHOT] error: {ex.Message}");
            IsConnected = false;
            CleanupSocket();
        }
    }

    private static async Task ReadExactlyAsync(NetworkStream s, byte[] buf, int len, CancellationToken ct)
    {
        int read = 0;
        while (read < len)
        {
            int n = await s.ReadAsync(buf.AsMemory(read, len - read), ct);
            if (n == 0) throw new IOException("Socket closed");
            read += n;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            var s = _stream!;
            var typeBuf = new byte[1];
            var payload12 = new byte[12];

            while (!ct.IsCancellationRequested && IsConnected)
            {
                await ReadExactlyAsync(s, typeBuf, 1, ct);
                byte type = typeBuf[0];

                if (type == (byte)MessageType.AuthState)
                {
                    await ReadExactlyAsync(s, payload12, 12, ct);

                    uint ack = BitConverter.ToUInt32(payload12, 0);
                    float x = BitConverter.ToSingle(payload12, 4);
                    float y = BitConverter.ToSingle(payload12, 8);

                    Log1Hz(ref _lastRecvLog, $"[NET RECV] Auth ack={ack} x={x:F1} y={y:F1}");
                    OnAuthState?.Invoke(ack, x, y);
                }
                else if (type == (byte)MessageType.JoinAck)
                {
                    await ReadExactlyAsync(s, payload12, 12, ct);

                    uint playerId = BitConverter.ToUInt32(payload12, 0);
                    float sx = BitConverter.ToSingle(payload12, 4);
                    float sy = BitConverter.ToSingle(payload12, 8);

                    Log1Hz(ref _lastRecvLog, $"[NET RECV] JoinAck pid={playerId} spawn=({sx:F1},{sy:F1})");
                    OnJoinAck?.Invoke(playerId, sx, sy);
                }
                else if (type == (byte)MessageType.RemoteState)
                {
                    await ReadExactlyAsync(s, payload12, 12, ct);

                    float x = BitConverter.ToSingle(payload12, 0);
                    float y = BitConverter.ToSingle(payload12, 4);
                    int maskInt = BitConverter.ToInt32(payload12, 8);

                    Log1Hz(ref _lastRecvLog, $"[NET RECV] Remote x={x:F1} y={y:F1} mask={maskInt}");
                    OnRemoteState?.Invoke(x, y, (StateList)maskInt);
                }
                else if (type == (byte)MessageType.RemoteShot)
                {
                    await ReadExactlyAsync(s, payload12, 12, ct);

                    int a = BitConverter.ToInt32(payload12, 0);
                    int b = BitConverter.ToInt32(payload12, 4);
                    int chargeInt = BitConverter.ToInt32(payload12, 8);

                    Log1Hz(ref _lastRecvLog, $"[NET RECV] RemoteShot a={a} b={b} ch={chargeInt}");
                    OnRemoteShot?.Invoke(a, b, chargeInt);
                }
                else
                {
                    Debug.WriteLine($"[NET] UNKNOWN TYPE {type}");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NET] receive error: {ex.Message}");
        }
        finally
        {
            IsConnected = false;
            CleanupSocket();
        }
    }

    private void CleanupSocket()
    {
        try { _stream?.Close(); } catch { }
        try { _tcpClient?.Close(); } catch { }
        _stream = null;
        _tcpClient = null;
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        CleanupSocket();
        try { _cts.Dispose(); } catch { }
    }
}