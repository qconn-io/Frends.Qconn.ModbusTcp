namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>High-level error category for routing in Process Exclusive Decision shapes.</summary>
public enum ErrorCategory
{
    /// <summary>No error.</summary>
    None,

    /// <summary>TCP connect or read timed out. Likely transient.</summary>
    Timeout,

    /// <summary>Connection was actively refused by the target host. Often transient.</summary>
    ConnectionRefused,

    /// <summary>Target host is unreachable at the network level. Often transient.</summary>
    HostUnreachable,

    /// <summary>Operation was cancelled via CancellationToken.</summary>
    Cancelled,

    /// <summary>Modbus slave returned an exception response (exception codes 1–11).</summary>
    ModbusException,

    /// <summary>Wire read succeeded but type decoding failed (e.g. invalid bytes for AsciiString).</summary>
    DecodingError,

    /// <summary>Invalid input parameters (bad address, count out of range, etc.). Not retryable.</summary>
    InvalidInput,

    /// <summary>Unexpected error not covered by other categories.</summary>
    Unexpected,
}

/// <summary>Structured error information returned when Success is false.</summary>
public class ErrorDetail
{
    /// <summary>High-level error category. Use this for Process routing logic.</summary>
    public ErrorCategory Category { get; }

    /// <summary>True if retrying the same request later is likely to succeed.
    /// True for: Timeout, ConnectionRefused, HostUnreachable, SlaveDeviceBusy (code 6), GatewayPathUnavailable (code 10),
    /// GatewayTargetDeviceFailedToRespond (code 11).
    /// False for: IllegalFunction (1), IllegalDataAddress (2), IllegalDataValue (3), DecodingError, InvalidInput, and others.</summary>
    public bool IsTransient { get; }

    /// <summary>Modbus exception code if the slave returned one (1–11 per spec), else null.</summary>
    public int? ModbusExceptionCode { get; }

    /// <summary>Socket error code string if the failure was at TCP level, else null.</summary>
    public string? SocketErrorCode { get; }

    /// <summary>Human-readable summary for logs. Do not route Process logic on this string — use Category instead.</summary>
    public string Message { get; }

    /// <summary>Initializes a new ErrorDetail.</summary>
    public ErrorDetail(ErrorCategory category, bool isTransient, string message,
        int? modbusExceptionCode = null, string? socketErrorCode = null)
    {
        Category = category;
        IsTransient = isTransient;
        Message = message;
        ModbusExceptionCode = modbusExceptionCode;
        SocketErrorCode = socketErrorCode;
    }
}
