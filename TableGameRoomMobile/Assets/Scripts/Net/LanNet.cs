using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

using TableGameRoom.Mobile.Core;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TableGameRoom.Mobile.Net;

// ==================== 数据模型 ====================

/// <summary>局域网内的一个玩家。</summary>
public sealed class PlayerInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsSessionHost { get; set; }
}

/// <summary>房间成员。</summary>
public sealed class RoomMember
{
    public string PlayerId { get; set; } = "";
    /// <summary>true=参战，false=观战。</summary>
    public bool Playing { get; set; } = true;
    /// <summary>房间内筹码余额（统一筹码制）。</summary>
    public int Chips { get; set; } = 10_000;
    /// <summary>无限筹码（房主默认为 true）。</summary>
    public bool Unlimited { get; set; }
    /// <summary>是否已出局（筹码归零）。</summary>
    public bool Busted { get; set; }

    /// <summary>是否有足够筹码下注（无限模式恒为 true）。</summary>
    public bool CanAfford(int amount) => Unlimited || Chips >= amount;

    /// <summary>扣减筹码（无限模式不扣）。</summary>
    public void Pay(int amount)
    {
        if (Unlimited) return;
        Chips = Math.Max(0, Chips - amount);
        if (Chips <= 0) { Busted = true; Playing = false; }
    }

    /// <summary>赢取筹码（无限模式不加）。</summary>
    public void Win(int amount)
    {
        if (Unlimited) return;
        Chips += amount;
    }
}

/// <summary>一个房间（对局层）。</summary>
public sealed class RoomInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string HostId { get; set; } = "";
    public List<RoomMember> Members { get; set; } = new List<RoomMember>();
    public string GameKey { get; set; } = "blackjack";
    public int TierIndex { get; set; } = 0;
    public int Chips { get; set; } = 10_000;
    public int TurnSeconds { get; set; } = 30;
    /// <summary>waiting | playing</summary>
    public string State { get; set; } = "waiting";

    /// <summary>本局总人数（含真人 + AI 补位）。</summary>
    public int Seats { get; set; } = 3;
    /// <summary>需要 AI 补位的数量（= Seats - 参战真人数）。</summary>
    public int AiFillCount => Math.Max(0, Seats - Members.Count(m => m.Playing));

    public bool IsFull => Members.Count >= 10;
}

/// <summary>整个局域网会话的完整状态（主机权威，广播给所有人）。</summary>
public sealed class SessionState
{
    public string SessionName { get; set; } = "";
    public int Port { get; set; }
    public List<PlayerInfo> Players { get; set; } = new List<PlayerInfo>();
    public List<RoomInfo> Rooms { get; set; } = new List<RoomInfo>();
}

/// <summary>网络消息（统一信封，newline-delimited JSON）。</summary>
public sealed class NetMessage
{
    public string Type { get; set; } = "";
    public string? PlayerId { get; set; }
    public string? PlayerName { get; set; }
    public string? RoomId { get; set; }
    public string? Text { get; set; }
    public string? GameKey { get; set; }
    public SessionState? Session { get; set; }
    public RoomInfo? Room { get; set; }
    public Dictionary<string, string>? Data { get; set; }
}

// ==================== 主机端（TCP 服务器 + 权威状态）====================

public sealed class LanServer : IDisposable
{
    private TcpListener? _listener;
    private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
    private readonly ConcurrentDictionary<string, string> _clientNames = new();
    private readonly ConcurrentDictionary<string, StreamWriter> _writers = new();
    private CancellationTokenSource? _cts;

    public SessionState State { get; } = new();
    public int Port { get; private set; }
    public string HostPlayerId { get; private set; } = "";

    /// <summary>收到某玩家发来的消息（除广播外的业务消息）。</summary>
    public event Action<string, NetMessage>? MessageReceived;

    /// <summary>玩家掉线。</summary>
    public event Action<string>? PlayerDisconnected;

    /// <summary>会话状态变化（有人加入/离开、房间变动）。主机 UI 靠它刷新自己的快照。</summary>
    public event Action? SessionChanged;

    public static int PickPort(int start = 47800)
    {
        for (var p = start; p < start + 200; p++)
        {
            try
            {
                var l = new TcpListener(IPAddress.Any, p);
                l.Start();
                l.Stop();
                return p;
            }
            catch { }
        }
        return start;
    }

    public void Start(string sessionName, string hostName)
    {
        State.SessionName = sessionName;
        Port = PickPort();
        State.Port = Port;

        HostPlayerId = Guid.NewGuid().ToString("N")[..8];
        var host = new PlayerInfo { Id = HostPlayerId, Name = hostName, IsSessionHost = true };
        State.Players.Add(host);

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync();
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(120, ct).ContinueWith(_ => { }); }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);
        var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        string? playerId = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line is null) break;
                if (line.Length == 0) continue;

                NetMessage? msg;
                try { msg = WireFormat.Decode(line); }
                catch { continue; }
                if (msg is null) continue;

                if (msg.Type == "hello")
                {
                    playerId = Guid.NewGuid().ToString("N")[..8];
                    var name = string.IsNullOrWhiteSpace(msg.PlayerName) ? "玩家" : msg.PlayerName!.Trim();
                    if (name.Length > 16) name = name[..16];

                    _clients[playerId] = client;
                    _clientNames[playerId] = name;
                    _writers[playerId] = writer;

                    lock (State.Players)
                        State.Players.Add(new PlayerInfo { Id = playerId, Name = name, IsSessionHost = false });

                    SendTo(playerId, new NetMessage { Type = "welcome", PlayerId = playerId, Session = Snapshot() });
                    BroadcastSession();
                    continue;
                }

                if (playerId is null) continue;
                msg.PlayerId = playerId;
                MessageReceived?.Invoke(playerId, msg);
            }
        }
        catch { }
        finally
        {
            if (playerId is not null) RemovePlayer(playerId);
        }
    }

    private void RemovePlayer(string playerId)
    {
        _clients.TryRemove(playerId, out _);
        _clientNames.TryRemove(playerId, out _);
        _writers.TryRemove(playerId, out _);

        lock (State.Players)
            State.Players.RemoveAll(p => p.Id == playerId);

        // 从所有房间移除；房主离开则转让或解散
        foreach (var room in State.Rooms.ToList())
        {
            room.Members.RemoveAll(m => m.PlayerId == playerId);
            if (room.HostId == playerId)
            {
                if (room.Members.Count > 0) room.HostId = room.Members[0].PlayerId;
                else State.Rooms.Remove(room);
            }
        }

        PlayerDisconnected?.Invoke(playerId);
        BroadcastSession();
    }

    public SessionState Snapshot()
    {
        var json = Json.Serialize(State);
        return Json.Deserialize<SessionState>(json) ?? new SessionState();
    }

    public void SendTo(string playerId, NetMessage msg)
    {
        if (!_writers.TryGetValue(playerId, out var w)) return;
        try
        {
            lock (w) w.WriteLine(WireFormat.Encode(msg));
        }
        catch { }
    }

    public void BroadcastSession()
    {
        var snapshot = Snapshot();
        var msg = new NetMessage { Type = "session", Session = snapshot };
        var payload = WireFormat.Encode(msg);
        foreach (var kv in _writers)
        {
            try { lock (kv.Value) kv.Value.WriteLine(payload); }
            catch { }
        }
        try { SessionChanged?.Invoke(); } catch { }
    }

    public void BroadcastRaw(NetMessage msg)
    {
        var payload = WireFormat.Encode(msg);
        foreach (var kv in _writers)
        {
            try { lock (kv.Value) kv.Value.WriteLine(payload); }
            catch { }
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        foreach (var c in _clients.Values) { try { c.Close(); } catch { } }
        _clients.Clear();
        _writers.Clear();
    }
}

// ==================== 客户端 ====================

public sealed class LanClient : IDisposable
{
    private TcpClient? _tcp;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    public string PlayerId { get; private set; } = "";
    public bool Connected => _tcp?.Connected == true;

    public event Action<NetMessage>? Received;
    public event Action? Disconnected;

    public async Task<bool> ConnectAsync(string host, int port, string playerName, int timeoutMs = 5000)
    {
        try
        {
            _tcp = new TcpClient();
            var connectTask = _tcp.ConnectAsync(host, port);
            var done = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
            if (done != connectTask || !_tcp.Connected) return false;

            var stream = _tcp.GetStream();
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            _cts = new CancellationTokenSource();

            Send(new NetMessage { Type = "hello", PlayerName = playerName });
            _ = ReceiveLoopAsync(stream, _cts.Token);
            return true;
        }
        catch { return false; }
    }

    private async Task ReceiveLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        var reader = new StreamReader(stream, Encoding.UTF8);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line is null) break;
                if (line.Length == 0) continue;

                NetMessage? msg;
                try { msg = WireFormat.Decode(line); }
                catch { continue; }
                if (msg is null) continue;

                if (msg.Type == "welcome" && msg.PlayerId is not null)
                    PlayerId = msg.PlayerId;

                Received?.Invoke(msg);
            }
        }
        catch { }
        finally { Disconnected?.Invoke(); }
    }

    public void Send(NetMessage msg)
    {
        try
        {
            if (_writer is null) return;
            lock (_writer) _writer.WriteLine(WireFormat.Encode(msg));
        }
        catch { }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _tcp?.Close(); } catch { }
    }
}

// ==================== UDP 局域网查找（按端口找主机 IP）====================

public static class LanDiscovery
{
    private const int QueryPort = 47899;

    /// <summary>主机端：监听查询，回复自己的 IP。</summary>
    public static void StartResponder(int sessionPort, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var udp = new UdpClient(QueryPort);
                while (!ct.IsCancellationRequested)
                {
                    var receiveTask = udp.ReceiveAsync();
                    var done = await Task.WhenAny(receiveTask, Task.Delay(500));
                    if (done != receiveTask) continue;
                    var result = await receiveTask;
                    var text = Encoding.UTF8.GetString(result.Buffer);
                    if (text != $"WHERE:{sessionPort}") continue;
                    var reply = Encoding.UTF8.GetBytes($"HERE:{sessionPort}");
                    await udp.SendAsync(reply, reply.Length, result.RemoteEndPoint);
                }
            }
            catch { }
        }, ct);
    }

    /// <summary>客户端：广播查询，返回找到的主机 IP（找不到返回 null）。</summary>
    public static async Task<string?> FindHostAsync(int sessionPort, int timeoutMs = 1500)
    {
        try
        {
            using var udp = new UdpClient();
            udp.EnableBroadcast = true;
            var query = Encoding.UTF8.GetBytes($"WHERE:{sessionPort}");
            await udp.SendAsync(query, query.Length, new IPEndPoint(IPAddress.Broadcast, QueryPort));

            var receive = udp.ReceiveAsync();
            var done = await Task.WhenAny(receive, Task.Delay(timeoutMs));
            if (done != receive) return null;

            var result = await receive;
            var text = Encoding.UTF8.GetString(result.Buffer);
            return text == $"HERE:{sessionPort}" ? result.RemoteEndPoint.Address.ToString() : null;
        }
        catch { return null; }
    }

    public static string LocalIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch { return "127.0.0.1"; }
    }
}
