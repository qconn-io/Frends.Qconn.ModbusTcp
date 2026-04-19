# Frends.Qconn.ModbusTcp

Read typed, scaled, endianness-corrected values from Modbus TCP slave devices.

[![build](https://github.com/FrendsPlatform/Frends.Qconn.ModbusTcp/actions/workflows/Read_test_on_main.yml/badge.svg)](https://github.com/FrendsPlatform/Frends.Qconn.ModbusTcp/actions/workflows/Read_test_on_main.yml)
![Coverage](https://app-github-custom-badges.azurewebsites.net/Badge?key=FrendsPlatform/Frends.Qconn.ModbusTcp/Frends.Qconn.ModbusTcp|main)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://opensource.org/licenses/MIT)

## Tasks

- [Read.ReadData](#readreaddata) — single block read
- [ReadBatch.ReadBatchData](#readbatchreadbatchdata) — multiple blocks over one connection

---

## Read.ReadData

Connects to a Modbus TCP slave, reads one block of registers or coils, decodes the values to the requested type, applies scale and offset, and returns a structured result.

### Input parameters

| Name | Type | Default | Description |
|------|------|---------|-------------|
| Host | string | `"127.0.0.1"` | IP address or hostname of the Modbus slave |
| Port | int | `502` | TCP port |
| UnitId | byte | `1` | Modbus unit/slave ID (1–247) |
| DataType | ModbusDataType | `HoldingRegisters` | Coils, DiscreteInputs, HoldingRegisters, InputRegisters |
| StartAddress | ushort | `0` | First register/coil address |
| NumberOfValues | ushort | `1` | Number of values (not registers — Float32 counts as 1) |
| ValueType | ModbusValueType | `Raw` | Decoding type (hidden for coil data types) |

### Options parameters

| Name | Type | Default | Description |
|------|------|---------|-------------|
| ByteOrder | ByteOrder | `BigEndian` | Register byte order (ABCD/DCBA/BADC/CDAB) |
| AddressingMode | AddressingMode | `ZeroBased` | ZeroBased or ModiconOneBased |
| Scale | double | `1.0` | Multiplier applied after decoding |
| Offset | double | `0.0` | Offset added after scaling |
| ConnectTimeoutMs | int | `5000` | TCP connect timeout in ms |
| ReadTimeoutMs | int | `5000` | Modbus read timeout in ms |
| ThrowOnFailure | bool | `false` | Throw exception instead of returning error Result |

### Result properties

| Name | Type | Description |
|------|------|-------------|
| Success | bool | True if read succeeded |
| Data | object | Typed array: `ushort[]`, `short[]`, `int[]`, `float[]`, `double[]`, `bool[]`, `string` |
| FirstValue | object | First element of Data (convenience) |
| RawRegisters | ushort[] | Raw register words (null for coil types) |
| Error | ErrorDetail | Populated on failure: Category, IsTransient, Message, ModbusExceptionCode |
| Diagnostics | Diagnostics | ConnectTimeMs, ReadTimeMs, TotalTimeMs, WireStartAddress, WireRegisterCount |

### ValueType reference

| ValueType | Registers per value | Return type |
|-----------|--------------------|----|
| Raw | 1 | `ushort[]` |
| Int16 | 1 | `short[]` |
| UInt16 | 1 | `ushort[]` (or `double[]` with scale) |
| Int32 | 2 | `int[]` (or `double[]` with scale) |
| UInt32 | 2 | `uint[]` (or `double[]` with scale) |
| Float32 | 2 | `float[]` |
| Float64 | 4 | `double[]` |
| AsciiString | N (NumberOfValues = register count) | `string` |

---

## ReadBatch.ReadBatchData

Opens one TCP connection and executes all items sequentially. A Modbus exception on one item fails that item but does not abort the batch. A socket-level failure aborts the entire batch.

### BatchInput parameters

| Name | Type | Description |
|------|------|-------------|
| Host, Port, UnitId | — | Same as Read |
| Items | BatchReadItem[] | List of named reads |

### BatchReadItem fields

| Name | Type | Description |
|------|------|-------------|
| Name | string | Key in the result Items dictionary |
| DataType | ModbusDataType | — |
| StartAddress | ushort | — |
| NumberOfValues | ushort | — |
| ValueType | ModbusValueType | — |
| Scale | double | Per-item scale (default 1.0) |
| Offset | double | Per-item offset (default 0.0) |

### BatchResult properties

| Name | Type | Description |
|------|------|-------------|
| Success | bool | False only on socket-level failure |
| Items | Dictionary<string, ReadOutcome> | Per-item results (Success, Data, Error) |
| Diagnostics | Diagnostics | Timing for the entire batch |
| Error | ErrorDetail | Set only on socket-level failure |

---

## Worked examples

### Energy meter — Schneider iEM3155

The iEM3155 exposes active power at Modicon address 40087 as Float32 in CDAB (LittleEndianWordSwap) byte order.

```json
{
  "Input": {
    "Host": "192.168.1.10",
    "Port": 502,
    "UnitId": 1,
    "DataType": "HoldingRegisters",
    "StartAddress": 40087,
    "NumberOfValues": 1,
    "ValueType": "Float32"
  },
  "Options": {
    "ByteOrder": "LittleEndianWordSwap",
    "AddressingMode": "ModiconOneBased"
  }
}
```

`result.FirstValue` → `float` (kW)

### Valve state — Siemens S7 coils

```json
{
  "Input": {
    "Host": "192.168.1.20",
    "DataType": "Coils",
    "StartAddress": 0,
    "NumberOfValues": 3
  }
}
```

`result.Data` → `bool[]` where `true` = open

### Batch poll — multiple registers in one connection

```json
{
  "Input": {
    "Host": "192.168.1.10",
    "Items": [
      { "Name": "activePower", "DataType": "HoldingRegisters", "StartAddress": 0, "NumberOfValues": 1, "ValueType": "Float32" },
      { "Name": "voltage",     "DataType": "HoldingRegisters", "StartAddress": 2, "NumberOfValues": 1, "ValueType": "Float32" },
      { "Name": "frequency",   "DataType": "HoldingRegisters", "StartAddress": 4, "NumberOfValues": 1, "ValueType": "Float32" }
    ]
  },
  "Options": { "ByteOrder": "LittleEndianWordSwap" }
}
```

`batchResult.Items["activePower"].FirstValue` → active power as `float`

---

## Byte-order troubleshooting

Modbus carries multi-register values in one of four byte orderings. Use the device datasheet to confirm the notation, then pick the matching option:

| ByteOrder | ABCD notation | Registers on wire | Typical devices |
|-----------|--------------|-------------------|-----------------|
| BigEndian | ABCD | reg0=AB, reg1=CD | Most PLCs (default) |
| LittleEndian | DCBA | reg0=DC, reg1=BA | Some older controllers |
| BigEndianByteSwap | BADC | reg0=BA, reg1=DC | Some Modicon variants |
| LittleEndianWordSwap | CDAB | reg0=CD, reg1=AB | Schneider PM/iEM, WattNode |

**Quick test**: read a known value (e.g., nominal voltage = 230.0f). If the decoded value looks like bit noise, try the next byte order until it matches.

---

## Error handling

Always check `result.Success` before using `result.Data`.

```csharp
if (!result.Success)
{
    if (result.Error.IsTransient)
        // Retry: Timeout, ConnectionRefused, SlaveDeviceBusy (code 6)
    else
        // Permanent: InvalidInput, IllegalDataAddress (code 2)
}
```

`ErrorCategory` values: `None`, `Timeout`, `ConnectionRefused`, `HostUnreachable`, `Cancelled`, `ModbusException`, `DecodingError`, `InvalidInput`, `Unexpected`.

Transient Modbus exception codes: 5 (Acknowledge), 6 (SlaveDeviceBusy), 10 (GatewayPathUnavailable), 11 (GatewayTargetDeviceFailedToRespond).

---

## Known limitations (v1)

- **Read-only**: no write tasks in v1.
- **TCP only**: no Modbus RTU or ASCII.
- **No connection pooling**: each `ReadData` call opens and closes a new TCP connection.
- **No built-in retry**: use Frends Agent retry loop or Frends process retry for transient errors.

---

## Building and testing

```bash
git clone https://github.com/FrendsPlatform/Frends.Qconn.ModbusTcp.git
cd Frends.Qconn.ModbusTcp
dotnet build
dotnet test
dotnet pack --configuration Release
```
