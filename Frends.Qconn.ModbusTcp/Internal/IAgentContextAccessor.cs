using System;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Abstraction over access to Frends Agent runtime context (Agent/Environment/Process identity).
/// The Task does not take a hard dependency on a Frends platform package; the default implementation uses reflection.</summary>
internal interface IAgentContextAccessor
{
    string? AgentName { get; }
    string? EnvironmentName { get; }
    string? ProcessName { get; }
    Guid? ProcessInstanceId { get; }
    string? InitiatedBy { get; }
}
