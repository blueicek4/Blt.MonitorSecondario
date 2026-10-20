using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Bluetech.Display.Agent.Vendita;

namespace Bluetech.Display.Agent.Server;

/// <summary>
/// I display collegati via WebSocket. Ogni display ha la sua coda: un display lento non rallenta gli altri; se resta
/// troppo indietro viene staccato, si ricollega e riceve lo stato completo (sync).
/// </summary>
public sealed class DisplayHub(ILogger<DisplayHub> log)
{
    readonly ConcurrentDictionary<long, DisplayClient> _clients = new();
    long _nextId;
    long _sent;

    public int ClientCount => _clients.Count;
    public long MessagesSent => Interlocked.Read(ref _sent);

    public DisplayClient Add(WebSocket socket, string remote)
    {
        var client = new DisplayClient(Interlocked.Increment(ref _nextId), socket, remote);
        _clients[client.Id] = client;
        log.LogInformation("Display collegato da {Remote} ({Count} in tutto)", remote, _clients.Count);
        return client;
    }

    public void Send(DisplayClient client, CashIn message) => Enqueue(client, message.ToString());

    public void Broadcast(IReadOnlyList<CashIn> messages)
    {
        foreach (var message in messages)
        {
            var json = message.ToString();
            foreach (var client in _clients.Values)
                Enqueue(client, json);
        }
    }

    void Enqueue(DisplayClient client, string json)
    {
        if (!client.Queue.Writer.TryWrite(json))
        {
            log.LogWarning("Display {Remote} troppo indietro: lo stacco, si ricollegherà", client.Remote);
            client.Abort();
        }
    }

    /// <summary>Spedisce la coda del display finché il collegamento resta aperto.</summary>
    public async Task RunAsync(DisplayClient client, CancellationToken cancel)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, client.Aborted);
        var sender = SendLoop(client, linked.Token);
        var receiver = ReceiveLoop(client, linked.Token);
        await Task.WhenAny(sender, receiver);
        linked.Cancel();
        _clients.TryRemove(client.Id, out _);
        try
        {
            if (client.Socket.State == WebSocketState.Open)
                await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "fine", CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
        log.LogInformation("Display {Remote} scollegato ({Count} ancora collegati)", client.Remote, _clients.Count);
    }

    async Task SendLoop(DisplayClient client, CancellationToken cancel)
    {
        try
        {
            await foreach (var json in client.Queue.Reader.ReadAllAsync(cancel))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await client.Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancel);
                Interlocked.Increment(ref _sent);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    static async Task ReceiveLoop(DisplayClient client, CancellationToken cancel)
    {
        // Il display non manda niente: si ascolta solo per accorgersi della chiusura.
        var buffer = new byte[1024];
        try
        {
            while (client.Socket.State == WebSocketState.Open)
            {
                var result = await client.Socket.ReceiveAsync(buffer, cancel);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }
}

public sealed class DisplayClient(long id, WebSocket socket, string remote)
{
    readonly CancellationTokenSource _abort = new();

    public long Id { get; } = id;
    public WebSocket Socket { get; } = socket;
    public string Remote { get; } = remote;
    const int QueueLimit = 1000;

    public Channel<string> Queue { get; } = Channel.CreateBounded<string>(new BoundedChannelOptions(QueueLimit)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,   // TryWrite fallisce a coda piena: il display viene staccato
    });
    public CancellationToken Aborted => _abort.Token;

    public void Abort()
    {
        Queue.Writer.TryComplete();
        _abort.Cancel();
    }
}
