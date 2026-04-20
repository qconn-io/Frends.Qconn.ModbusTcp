using System.ComponentModel;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Write.Definitions;

/// <summary>Options for Modbus TCP write operations. Inherits from the read Options and overrides the safety defaults:
/// ThrowOnFailure defaults to true (silent write failures to control systems are hazardous),
/// and Retry.MaxAttempts defaults to 1 (retrying writes requires explicit opt-in due to non-idempotency).</summary>
public class WriteOptions : Options
{
    public WriteOptions()
    {
        ThrowOnFailure = true;
        Retry = new RetryOptions { MaxAttempts = 1 };
    }

    /// <summary>Override to allow retry of write operations. Default is 1 (no retry). Set to 2+ only if the target
    /// register is idempotent — otherwise a retried write may apply its effect twice.</summary>
    [DefaultValue(false)]
    public bool AllowRetry { get; set; } = false;
}
