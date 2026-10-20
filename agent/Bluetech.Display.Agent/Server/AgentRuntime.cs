using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Bluetech.Display.Agent.Impostazioni;

namespace Bluetech.Display.Agent.Server;

/// <summary>Com'è partito l'Agent: cartelle, rete e HTTPS valgono fino al prossimo riavvio.</summary>
public sealed class AgentRuntime
{
    public required string DataFolder { get; init; }
    public required string LogFolder { get; init; }
    public required string DisplayFolder { get; init; }

    /// <summary>Le impostazioni di rete con cui l'Agent ascolta adesso.</summary>
    public required NetworkSettings StartupNetwork { get; init; }

    /// <summary>--porta al posto di quella delle impostazioni.</summary>
    public int? PortOverride { get; init; }

    public X509Certificate2? Certificate { get; init; }
    public string? AuthorityPath { get; init; }
    public string? HttpsProblem { get; init; }

    /// <summary>Porta, apertura in rete o HTTPS cambiati dal back office: valgono dal prossimo avvio.</summary>
    public bool RestartNeeded(AgentSettings now) =>
        JsonSerializer.Serialize(now.Rete, AgentSettings.Json) != JsonSerializer.Serialize(StartupNetwork, AgentSettings.Json);
}
