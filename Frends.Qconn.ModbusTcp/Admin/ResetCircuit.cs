using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Admin.Definitions;
using Frends.Qconn.ModbusTcp.Common.Definitions;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Admin;

/// <summary>Frends Task for manually resetting the circuit breaker of a device,
/// e.g. after an operator has confirmed the device is back online.</summary>
public static class ResetCircuit
{
    /// <summary>Forces the breaker for the identified device back to Closed state with zero failure count.
    /// If no breaker has been registered for the device yet, returns Reset=false (no action taken).</summary>
    /// <param name="input">Device identity (Host, Port, UnitId).</param>
    /// <param name="cancellationToken">Propagated from the Frends Agent.</param>
    /// <returns>A ResetCircuitResult indicating whether the breaker existed and was reset.</returns>
    public static Task<ResetCircuitResult> ResetState(
        [PropertyTab] CircuitStateInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = new ConnectionKey(input.Host, input.Port, input.UnitId,
            TransportMode.TcpNative, TransportSecurity.None, null, null);

        bool reset = BreakerRegistry.Reset(key);

        var ctx = AuditRouter.AgentContext;
        AuditRouter.Emit(new ModbusAuditEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            AgentName = ctx.AgentName,
            EnvironmentName = ctx.EnvironmentName,
            ProcessName = ctx.ProcessName,
            ProcessInstanceId = ctx.ProcessInstanceId,
            InitiatedBy = ctx.InitiatedBy,
            Operation = "ResetCircuit",
            Host = input.Host,
            Port = input.Port,
            UnitId = input.UnitId,
            FunctionCode = 0,
            Success = true,
            AttemptCount = 1,
            TotalTimeMs = 0,
        });

        return Task.FromResult(new ResetCircuitResult(reset));
    }
}
