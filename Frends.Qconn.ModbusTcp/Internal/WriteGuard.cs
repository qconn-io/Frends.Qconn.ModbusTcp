using System;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Checks Options.AllowWrites at the top of every Write Task before any socket is opened.
/// AllowWrites is a Frends task parameter — set it to #env.GroupName.AllowWrites in the Process editor
/// (e.g. #env.Modbus.AllowWrites) and configure the Frends Environment Variable per Environment to gate
/// writes without changing Process logic.</summary>
internal static class WriteGuard
{
    public static void EnsureAllowed(bool allowWrites)
    {
        if (!allowWrites)
            throw new InvalidOperationException(
                "Modbus writes are disabled (Options.AllowWrites = false). " +
                "To enable: set Options.AllowWrites = true, or use a Frends Environment Variable — " +
                "in the Frends Management portal add a variable (e.g. group Modbus, name AllowWrites, value true), " +
                "then set Options.AllowWrites = #env.Modbus.AllowWrites in the Process editor.");
    }
}
