using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Write.Definitions;

/// <summary>A single write entry in a WriteBatchInput.</summary>
public class WriteBatchItem
{
    /// <summary>Dictionary key in WriteBatchResult.Items for this item.</summary>
    [DefaultValue("\"\"")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Target data type. Coils → FC15. HoldingRegisters → FC06 if NumberOfValues=1 and ValueType=UInt16/Int16/Raw; FC16 otherwise.</summary>
    [DefaultValue(ModbusDataType.HoldingRegisters)]
    public ModbusDataType DataType { get; set; } = ModbusDataType.HoldingRegisters;

    /// <summary>Start address.</summary>
    [DefaultValue(0)]
    public ushort StartAddress { get; set; } = 0;

    /// <summary>Number of values (coils) to write, or number of typed registers (AsciiString → register count).</summary>
    [DefaultValue(1)]
    public ushort NumberOfValues { get; set; } = 1;

    /// <summary>Value encoding for register writes.</summary>
    [DefaultValue(ModbusValueType.Raw)]
    public ModbusValueType ValueType { get; set; } = ModbusValueType.Raw;

    /// <summary>Linear scale inverse is applied when encoding: raw = (value - Offset) / Scale.</summary>
    [DefaultValue(1.0)]
    public double Scale { get; set; } = 1.0;

    /// <summary>Offset (see Scale).</summary>
    [DefaultValue("0")]
    [DisplayFormat(DataFormatString = "Expression")]
    public double Offset { get; set; } = 0.0;

    /// <summary>Values to write.</summary>
    public object? Values { get; set; }
}
