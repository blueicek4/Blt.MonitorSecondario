using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Bluetech.Display.Agent.Aspetto;
using Bluetech.Display.Agent.Impostazioni;
using Bluetech.Display.Agent.Vendita;

namespace Bluetech.Display.Agent.Server;

/// <summary>
/// I display collegati via WebSocket, ognuno con la sua cassa. Ogni display ha la sua coda: un display lento non
/// rallenta gli altri; se resta troppo indietro viene staccato, si ricollega e riceve lo stato completo (sync).
/// </summary>
public sealed class DisplayHub(ILogger<DisplayHub> log)
{
    readonly ConcurrentDictionary<long, DisplayClient> _clients = new();
    long _nextId;
    long _sent;

    public int ClientCount => _clients.Count;
    public long MessagesSent => Interlocked.Read(ref _sent);
    public IReadOnlyCollection<DisplayClient> Clients => _clients.Values.ToArray();

    public DisplayClient Add(WebSocket socket, string remote, string cashPoint, string? deviceId, string name, bool canPrint, ScreenOverride? screenOverride = null)
    {
        var client = new DisplayClient(Interlocked.Increment(ref _nextId), socket, remote)
        {
            CashPoint = cashPoint,
            DeviceId = deviceId,
            Name = name,
            CanPrint = canPrint,
            Override = screenOverride,
        };
        _clients[client.Id] = client;
        log.LogInformation("Display {Name} collegato da {Remote} ({CashPoint}, {Count} in tutto)", name, remote, cashPoint, _clients.Count);
        return client;
    }

    public void Send(DisplayClient client, CashIn message) => Enqueue(client, ForClient(message.ToJson(), client).ToJsonString());

    /// <summary>I messaggi di una cassa, ai soli display di quella cassa.</summary>
    public void Broadcast(string cashPoint, IReadOnlyList<CashIn> messages)
    {
        foreach (var message in messages)
        {
            var json = message.ToJson();
            string? withButton = null;
            string? withoutButton = null;
            foreach (var client in _clients.Values)
            {
                if (client.CashPoint != cashPoint)
                    continue;
                if (message is ConfigMessage)
                    Enqueue(client, ForClient(json, client).ToJsonString());   // ogni schermo ha le sue eccezioni
                else
                    Enqueue(client, client.CanPrint
                        ? withButton ??= ForClient(json, client).ToJsonString()
                        : withoutButton ??= ForClient(json, client).ToJsonString());
            }
        }
    }

    /// <summary>A tutti i display (il battito).</summary>
    public void BroadcastAll(CashIn message)
    {
        var json = message.ToString();
        foreach (var client in _clients.Values)
            Enqueue(client, json);
    }

    /// <summary>
    /// Stacca i display scelti (dispositivo revocato, spostato su un'altra cassa, cassa tolta): si ricollegano e
    /// ricevono le impostazioni nuove, o il rifiuto.
    /// </summary>
    public int Disconnect(Func<DisplayClient, bool> which)
    {
        var count = 0;
        foreach (var client in _clients.Values)
        {
            if (!which(client))
                continue;
            client.Abort();
            count++;
        }
        return count;
    }

    /// <summary>
    /// Il cfg con le eccezioni di questo schermo. Il pulsante della copia cartacea c'è solo sugli schermi con una
    /// stampante: per gli altri <c>paperCopy</c> è falso, nello scontrino digitale e nello stato completo.
    /// </summary>
    static JsonObject ForClient(JsonObject json, DisplayClient client)
    {
        if (json["t"]?.GetValue<string>() == "cfg")
            return ScreenConfig.ForScreen(json, client.Override, client.CanPrint);
        if (client.CanPrint)
            return json;
        if (json["t"]?.GetValue<string>() == "receipt.digital" && json["paperCopy"] is not null)
        {
            var copy = json.DeepClone().AsObject();
            copy["paperCopy"] = false;
            return copy;
        }
        if (json["t"]?.GetValue<string>() == "sync" && json["state"]?["receipt"] is JsonObject)
        {
            var copy = json.DeepClone().AsObject();
            copy["state"]!["receipt"]!["paperCopy"] = false;
            return copy;
        }
        return json;
    }

    void Enqueue(DisplayClient client, string json)
    {
        if (!client.Queue.Writer.TryWrite(json) && !client.Closing)
        {
            log.LogWarning("Display {Remote} troppo indietro: lo stacco, si ricollegherà", client.Remote);
            client.Abort();
        }
    }

    /// <summary>Il display manda solo comandi brevi (la richiesta della copia cartacea): il resto si scarta.</summary>
    const int MaxIncoming = 1024;

    /// <summary>
    /// Spedisce la coda del display finché il collegamento resta aperto, il display lo chiude o <paramref name="cancel"/>
    /// scatta (richiesta finita, Agent che si ferma). I messaggi del display vanno a <paramref name="onMessage"/>.
    /// </summary>
    public async Task RunAsync(DisplayClient client, CancellationToken cancel, Func<DisplayClient, JsonObject, Task>? onMessage = null)
    {
        using var loops = CancellationTokenSource.CreateLinkedTokenSource(client.Aborted);
        var sender = SendLoop(client, loops.Token);
        var receiver = ReceiveLoop(client, loops.Token, onMessage);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancel.Register(() => ended.TrySetResult()))
            await Task.WhenAny(sender, receiver, ended.Task);
        _clients.TryRemove(client.Id, out _);

        // Chiusura ordinata, al massimo 2 s: si finisce di spedire, poi il frame di chiusura (la risposta a quello del
        // browser, o l'avviso che l'Agent si ferma) e si aspetta la risposta. Annullare la ricezione prima chiuderebbe il
        // collegamento di colpo.
        client.Closing = true;
        client.Queue.Writer.TryComplete();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await sender.WaitAsync(timeout.Token);
            if (client.Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await client.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "fine", timeout.Token);
            await receiver.WaitAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
        loops.Cancel();
        await Task.WhenAll(sender, receiver);
        log.LogInformation("Display {Name} scollegato ({Count} ancora collegati)", client.Name, _clients.Count);
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

    async Task ReceiveLoop(DisplayClient client, CancellationToken cancel, Func<DisplayClient, JsonObject, Task>? onMessage)
    {
        // Si ascolta per accorgersi della chiusura e per i comandi del display; un messaggio troppo lungo o non JSON si
        // scarta, il collegamento resta.
        var buffer = new byte[MaxIncoming];
        var message = new MemoryStream();
        var tooLong = false;
        try
        {
            while (client.Socket.State == WebSocketState.Open)
            {
                var result = await client.Socket.ReceiveAsync(buffer, cancel);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                if (message.Length + result.Count > MaxIncoming)
                    tooLong = true;
                else
                    message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                    continue;
                if (tooLong)
                    log.LogWarning("Display {Remote}: messaggio troppo lungo, scartato", client.Remote);
                else if (result.MessageType == WebSocketMessageType.Text && onMessage != null && Parse(message) is { } json)
                    _ = Handle(onMessage, client, json);   // una stampa lenta non blocca la ricezione né la chiusura
                message.SetLength(0);
                tooLong = false;
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    async Task Handle(Func<DisplayClient, JsonObject, Task> onMessage, DisplayClient client, JsonObject json)
    {
        try
        {
            await onMessage(client, json);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Display {Remote}: errore gestendo un comando", client.Remote);
        }
    }

    JsonObject? Parse(MemoryStream message)
    {
        try
        {
            return JsonNode.Parse(message.ToArray()) as JsonObject;
        }
        catch (JsonException)
        {
            log.LogWarning("Display: messaggio che non è JSON, scartato");
            return null;
        }
    }
}

public sealed class DisplayClient(long id, WebSocket socket, string remote)
{
    readonly CancellationTokenSource _abort = new();

    public long Id { get; } = id;
    public WebSocket Socket { get; } = socket;
    public string Remote { get; } = remote;
    /// <summary>La cassa che il display mostra.</summary>
    public required string CashPoint { get; init; }
    /// <summary>Lo schermo autorizzato; null per l'anteprima dal back office.</summary>
    public string? DeviceId { get; init; }
    public required string Name { get; init; }
    /// <summary>Lo schermo ha una stampante per la copia cartacea (in modalità demo, anche quella di prova).</summary>
    public bool CanPrint { get; set; }
    /// <summary>Le eccezioni dello schermo (disposizione, animazioni, attrazione, larghezza del conto).</summary>
    public ScreenOverride? Override { get; set; }
    const int QueueLimit = 1000;

    public Channel<string> Queue { get; } = Channel.CreateBounded<string>(new BoundedChannelOptions(QueueLimit)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,   // TryWrite fallisce a coda piena: il display viene staccato
    });
    public CancellationToken Aborted => _abort.Token;
    /// <summary>Il collegamento si sta chiudendo: la coda chiusa non vuol dire "troppo indietro".</summary>
    public volatile bool Closing;
    /// <summary>Quando il display ha mandato l'ultimo comando (Environment.TickCount64): uno ogni 2 s al massimo.</summary>
    public long LastCommandAt = long.MinValue / 2;

    public void Abort()
    {
        Queue.Writer.TryComplete();
        _abort.Cancel();
    }
}
