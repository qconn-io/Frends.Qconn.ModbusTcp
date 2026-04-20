using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Write.Definitions;

/// <summary>Input for ReadWriteMultiple (Modbus FC23): atomically write a block and read another block
/// in a single Modbus request (same UnitId / connection).</summary>
public class ReadWriteMultipleInput
{
    /// <summary>Target Modbus device host.</summary>
    /// <example>192.168.1.100</example>
    [DefaultValue("\"127.0.0.1\"")]
    [DisplayFormat(DataFormatString = "Text")]
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>TCP port.</summary>
    [DefaultValue(502)]
    public int Port { get; set; } = 502;

    /// <summary>Modbus unit ID.</summary>
    [DefaultValue(1)]
    public byte UnitId { get; set; } = 1;

    /// <summary>Start address of the register block to read.</summary>
    [DefaultValue(0)]
    public ushort ReadStartAddress { get; set; } = 0;

    /// <summary>Number of registers to read (1..125).</summary>
    [DefaultValue(1)]
    public ushort ReadRegisterCount { get; set; } = 1;

    /// <summary>Start address of the register block to write.</summary>
    [DefaultValue(0)]
    public ushort WriteStartAddress { get; set; } = 0;

    /// <summary>Values to write. Encoded as raw UInt16 per register.</summary>
    [DefaultValue(null)]
    public ushort[]? WriteRegisters { get; set; }
}
