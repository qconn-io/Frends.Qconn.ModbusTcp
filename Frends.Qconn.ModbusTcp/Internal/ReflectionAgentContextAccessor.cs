using System;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Default agent-context accessor. Reads environment variables set by the Frends Agent at Process run,
/// with fall-throughs to the machine name. No hard NuGet dependency on Frends runtime packages; a richer
/// reflection-based accessor is wired when Frends Shared State / runtime reflection becomes available.</summary>
internal sealed class ReflectionAgentContextAccessor : IAgentContextAccessor
{
    public string? AgentName => Environment.GetEnvironmentVariable("FRENDS_AGENT_NAME") ?? Environment.MachineName;

    public string? EnvironmentName => Environment.GetEnvironmentVariable("FRENDS_ENVIRONMENT_NAME")
        ?? Environment.GetEnvironmentVariable("FRENDS_ENVIRONMENT");

    public string? ProcessName => Environment.GetEnvironmentVariable("FRENDS_PROCESS_NAME");

    public Guid? ProcessInstanceId
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("FRENDS_PROCESS_INSTANCE_ID");
            return Guid.TryParse(raw, out var g) ? g : (Guid?)null;
        }
    }

    public string? InitiatedBy => Environment.GetEnvironmentVariable("FRENDS_INITIATED_BY");
}
