using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Admin.Definitions;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Admin;

/// <summary>Frends Task for inspecting the circuit-breaker state of a single device.</summary>
public static class CircuitState
{
    /// <summary>Returns the breaker state for the identified device, or Exists=false if no breaker is tracked.
    /// A breaker is tracked once the first operation against the device has been attempted.</summary>
    /// <param name="input">Device identity (Host, Port, UnitId).</param>
    /// <param name="cancellationToken">Propagated from the Frends Agent.</param>
    /// <returns>A CircuitStateResult snapshot.</returns>
    public static Task<CircuitStateResult> GetState(
        [PropertyTab] CircuitStateInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = new ConnectionKey(input.Host, input.Port, input.UnitId,
            TransportMode.TcpNative, TransportSecurity.None, null, null);

        var snapshot = BreakerRegistry.SnapshotFor(key);
        if (snapshot is null)
            return Task.FromResult(new CircuitStateResult(exists: false, state: null,
                failureCount: 0, lastFailureUtc: null, openUntilUtc: null));

        return Task.FromResult(new CircuitStateResult(
            exists: true,
            state: snapshot.State.ToString(),
            failureCount: snapshot.FailureCount,
            lastFailureUtc: snapshot.LastFailureUtc,
            openUntilUtc: snapshot.OpenUntilUtc));
    }
}
