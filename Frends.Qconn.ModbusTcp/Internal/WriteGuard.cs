using System;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Checks the Environment Variable ModbusWritesAllowed at the top of every Write Task.
/// Throws InvalidOperationException (before any socket is opened) when writes are disabled.
/// Default when the env var is unset is 'true' for v1 backward compatibility — Production Environments
/// are strongly recommended to set this to 'false' and only enable in dedicated control-plane Environments.</summary>
internal static class WriteGuard
{
    public const string EnvVar = "ModbusWritesAllowed";

    public static void EnsureAllowed()
    {
        var raw = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(raw)) return; // unset → allowed (backward compat)
        if (bool.TryParse(raw, out var allowed) && allowed) return;
        if (raw.Trim().Equals("1", StringComparison.Ordinal)) return;

        throw new InvalidOperationException(
            "Modbus writes are disabled for this Environment (ModbusWritesAllowed=false). " +
            "Configure this Environment Variable to enable writes.");
    }
}
