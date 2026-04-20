using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Write.Definitions;

/// <summary>Input for Modbus TCP write operations. Reuses the v1 Input's Host/Port/UnitId/DataType/StartAddress/NumberOfValues/ValueType
/// and adds a Values payload.</summary>
public class WriteInput : Input
{
    /// <summary>Values to write. Shape depends on the Write Task:
    /// WriteSingleCoil: bool.
    /// WriteSingleRegister: one numeric value of the chosen ValueType.
    /// WriteMultiple against coils: bool[].
    /// WriteMultiple against registers: numeric array of the chosen ValueType (or a string for AsciiString).
    /// </summary>
    /// <example>42</example>
    [DefaultValue(null)]
    public object? Values { get; set; }
}
