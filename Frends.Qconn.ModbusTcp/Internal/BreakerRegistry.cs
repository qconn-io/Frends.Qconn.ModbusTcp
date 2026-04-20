using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Agent-wide registry of circuit breakers, keyed by ConnectionKey.
/// One breaker per device; survives for the Agent process lifetime.</summary>
internal static class BreakerRegistry
{
    private static readonly ConcurrentDictionary<ConnectionKey, CircuitBreaker> Breakers = new();
    private static Func<DateTimeOffset> clock = () => DateTimeOffset.UtcNow;

    /// <summary>Gets or creates the breaker for this key. The first caller's options shape the breaker;
    /// subsequent callers reuse the same instance regardless of their options (simple contract).</summary>
    public static CircuitBreaker Get(ConnectionKey key, CircuitBreakerOptions opts)
        => Breakers.GetOrAdd(key, _ => new CircuitBreaker(opts));

    public static Func<DateTimeOffset> Clock => clock;

    /// <summary>Snapshot for the CircuitState admin Task.</summary>
    public static IReadOnlyList<(ConnectionKey Key, BreakerSnapshot Snapshot)> SnapshotAll()
        => Breakers.Select(kv => (kv.Key, kv.Value.Snapshot())).ToArray();

    public static BreakerSnapshot? SnapshotFor(ConnectionKey key)
        => Breakers.TryGetValue(key, out var b) ? b.Snapshot() : null;

    /// <summary>Manually resets a breaker. Used by the ResetCircuit admin Task.</summary>
    public static bool Reset(ConnectionKey key)
    {
        if (Breakers.TryGetValue(key, out var b))
        {
            b.Reset();
            return true;
        }
        return false;
    }

    /// <summary>Test-only: drains all breakers and replaces the clock.</summary>
    internal static void ResetForTests(Func<DateTimeOffset>? newClock = null)
    {
        Breakers.Clear();
        if (newClock != null) clock = newClock;
        else clock = () => DateTimeOffset.UtcNow;
    }
}
