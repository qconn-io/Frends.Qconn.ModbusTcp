# Frends.Qconn.ModbusTcp

Read and write typed, scaled, endianness-corrected values on Modbus TCP slave devices.

[![build](https://github.com/qconn-io/Frends.Qconn.ModbusTcp/actions/workflows/Read_test_on_main.yml/badge.svg)](https://github.com/qconn-io/Frends.Qconn.ModbusTcp/actions/workflows/Read_test_on_main.yml)
![Coverage](https://app-github-custom-badges.azurewebsites.net/Badge?key=qconn-io/Frends.Qconn.ModbusTcp/Frends.Qconn.ModbusTcp|main)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://opensource.org/licenses/MIT)

---

## Getting started

### One-time setup for Write tasks

Write tasks have an **Allow Writes** option that defaults to `true`. To control access per Environment without touching Process logic, use a Frends Environment Variable:

**Step 1 — Create the variable.** In the **Frends Management portal**, go to **Environments → [your Environment] → Variables** and add a variable. Group Name and Variable Name are both required — pick names consistent with your team's conventions. Example:

| Group Name | Variable Name | Value |
|------------|--------------|-------|
| `Modbus` | `AllowWrites` | `true` or `false` |

Set it to `false` on read-only Environments (monitoring, reporting). Set it to `true` on control-plane Environments that need to write.

**Step 2 — Reference it in the Process.** In each Write Task's **Options tab**, set **Allow Writes** to:

```
#env.Modbus.AllowWrites
```

Replace `Modbus` with whatever Group Name you chose in Step 1.

The task throws `InvalidOperationException` before opening any TCP connection when Allow Writes is false, so the safeguard has zero network cost.

### Your first Read process (2 steps)

**Step 1 — Read.ReadData**

| Field | Value |
|-------|-------|
| Host | `192.168.1.10` |
| DataType | `HoldingRegisters` |
| StartAddress | `0` |
| NumberOfValues | `1` |
| ValueType | `Float32` |

**Step 2 — use the result**

```
[ReadPower].result.Success      → true/false
[ReadPower].result.FirstValue   → the decoded float
[ReadPower].result.Error        → populated only when Success is false
```

Always check `Success` before consuming `FirstValue` or `Data`.

### Your first Write process (3 steps)

**Step 1 — Check the device is reachable (optional but recommended)**

Use `Admin.CircuitState.GetState` to confirm the breaker is Closed before writing. Skip in low-stakes scenarios.

**Step 2 — Write.WriteSingleRegister.WriteData**

| Field | Value |
|-------|-------|
| Host | `192.168.1.10` |
| StartAddress | `100` |
| ValueType | `Float32` |
| Value | `23.5` |

**Step 3 — Check the result**

```
[WriteSetpoint].result.Success              → true/false
[WriteSetpoint].result.WireRegistersWritten → number of registers written
[WriteSetpoint].result.Error           → populated only when Success is false
```

Write tasks default `ThrowOnFailure = true`, so an unhandled write failure stops the Process. Set `ThrowOnFailure = false` if you want to inspect the error and decide yourself.

---

## Which task for which job?

| I want to… | Task |
|------------|------|
| Read one block of registers or coils | **Read.ReadData** |
| Read several blocks in one TCP connection | **ReadBatch.ReadBatchData** |
| Force a single coil on or off (digital output) | **WriteSingleCoil.WriteData** |
| Write one register — setpoint, mode, parameter | **WriteSingleRegister.WriteData** |
| Write a contiguous block of registers or coils | **WriteMultiple.WriteData** |
| Read some registers AND write others in one round trip | **ReadWriteMultiple.ReadWriteData** |
| Execute several independent writes over one connection | **WriteBatch.WriteBatchData** |
| Build an Ops dashboard showing live connection counts | **PoolStatistics.GetStatistics** |
| Check whether a device has tripped the circuit breaker | **CircuitState.GetState** |
| Manually clear a tripped breaker without restarting | **ResetCircuit.ResetState** |

**Write task selector:**

```
Write one coil?           → WriteSingleCoil
Write one register?       → WriteSingleRegister
Write a block?            → WriteMultiple
Read + write atomically?  → ReadWriteMultiple
Multiple unrelated writes → WriteBatch
```

---

## Process recipes

### Recipe 1 — Poll an energy meter (triggered or scheduled)

Reads active power, voltage, and frequency from a Schneider iEM3155 in one TCP connection.

**Step 1 — ReadBatch.ReadBatchData**

```json
{
  "Input": {
    "Host": "192.168.1.10",
    "Items": [
      { "Name": "activePower", "DataType": "HoldingRegisters", "StartAddress": 40087, "NumberOfValues": 1, "ValueType": "Float32" },
      { "Name": "voltage",     "DataType": "HoldingRegisters", "StartAddress": 40071, "NumberOfValues": 1, "ValueType": "Float32" },
      { "Name": "frequency",   "DataType": "HoldingRegisters", "StartAddress": 40089, "NumberOfValues": 1, "ValueType": "Float32" }
    ]
  },
  "Options": {
    "ByteOrder": "LittleEndianWordSwap",
    "AddressingMode": "ModiconOneBased"
  }
}
```

**Step 2 — branch on success**

```
if [PollMeter].result.Success = false → go to error handling step
```

**Step 3 — use per-item results**

```
[PollMeter].result.Items["activePower"].FirstValue   → kW (float)
[PollMeter].result.Items["voltage"].FirstValue       → V  (float)
[PollMeter].result.Items["frequency"].FirstValue     → Hz (float)
```

Each item has its own `.Success` and `.Error` — a failed read on one item does not abort the others.

---

### Recipe 2 — Update a PLC setpoint

Writes a temperature setpoint to a Siemens S7 holding register.

**Step 1 — Write.WriteSingleRegister.WriteData**

```json
{
  "Input": {
    "Host": "192.168.1.20",
    "UnitId": 1,
    "StartAddress": 100,
    "ValueType": "Float32",
    "Value": 75.0
  },
  "Options": {
    "ByteOrder": "BigEndian"
  }
}
```

**Step 2 — check the result**

```
[WriteTemp].result.Success                → must be true before assuming the device accepted it
[WriteTemp].result.WireRegistersWritten   → should be 2 (one Float32 = two registers)
```

> **Note:** `ThrowOnFailure` defaults to `true` for all Write tasks. Remove the check step and rely on the Process exception path if that suits your flow. Switch to `false` only when you need to inspect the error and continue.

---

### Recipe 3 — Toggle a valve or digital output

Turns coil 4 on, waits, then turns it off. Useful for pulse outputs or one-shot actuators.

**Step 1 — Write.WriteSingleCoil.WriteData (ON)**

```json
{
  "Input": {
    "Host": "192.168.1.30",
    "StartAddress": 4,
    "Value": true
  }
}
```

**Step 2 — Frends Delay connector** — wait however long the pulse must be held

**Step 3 — Write.WriteSingleCoil.WriteData (OFF)**

Same config as Step 1 but `"Value": false`.

---

### Recipe 4 — Safe read-modify-write (atomic, one round trip)

Reads the current setpoint, adds an offset, and writes back — all in a single FC23 transaction so no other master can interleave.

**Step 1 — Write.ReadWriteMultiple.ReadWriteData**

```json
{
  "Input": {
    "Host": "192.168.1.10",
    "ReadStartAddress": 200,
    "ReadRegisterCount": 2,
    "WriteStartAddress": 200,
    "WriteRegisters": [100, 200]
  }
}
```

**Step 2 — use the read-back to confirm**

```
[AtomicUpdate].result.ReadRegisters   → raw register values read before the write
[AtomicUpdate].result.Success         → overall success flag
```

Use this pattern when two Processes share a device and you need to avoid a read–compute–write race.

---

### Recipe 5 — Write multiple parameters in one trip

Sends five setpoints to a device in a single FC16 transaction. Faster and more consistent than five separate Write calls.

**Step 1 — Write.WriteMultiple.WriteData**

```json
{
  "Input": {
    "Host": "192.168.1.10",
    "DataType": "HoldingRegisters",
    "StartAddress": 300,
    "ValueType": "Int16",
    "Values": [100, 200, 150, 50, 250]
  }
}
```

All values must be the same `ValueType`. For mixed types, use `WriteBatch` instead.

---

### Recipe 6 — Mixed-type batch write (multiple devices or registers in one step)

Writes a float setpoint to one address and an integer mode flag to another, over one connection.

**Step 1 — Write.WriteBatch.WriteBatchData**

```json
{
  "Input": {
    "Host": "192.168.1.10",
    "Items": [
      { "Name": "setpoint", "DataType": "HoldingRegisters", "StartAddress": 100, "ValueType": "Float32", "Values": [73.5] },
      { "Name": "mode",     "DataType": "HoldingRegisters", "StartAddress": 110, "ValueType": "Int16",   "Values": [2]    },
      { "Name": "enable",   "DataType": "Coils",            "StartAddress": 0,   "Values": [true]                        }
    ]
  }
}
```

**Step 2 — inspect per-item outcomes**

```
[BatchWrite].result.Items["setpoint"].Success   → true/false
[BatchWrite].result.Items["mode"].Success       → true/false
[BatchWrite].result.Items["enable"].Success     → true/false
```

A Modbus exception on one item fails that item but does not abort the remaining items. A socket-level failure aborts the entire batch.

---

### Recipe 7 — Device health check before a critical write

Checks the breaker state before writing to a high-stakes register. Avoids sending a write that will immediately short-circuit.

**Step 1 — Admin.CircuitState.GetState**

```json
{
  "Input": {
    "Host": "192.168.1.10",
    "Port": 502,
    "UnitId": 1
  }
}
```

**Step 2 — branch**

```
if [CheckBreaker].result.State = "Open"
    → go to AlertOps step (send notification, do not write)
else
    → go to WriteSetpoint step
```

**Step 3 — AlertOps** — send email/Teams/Slack alert including:

```
Device: [CheckBreaker].result.Host (pass through from input)
Failure count: [CheckBreaker].result.FailureCount
Open until: [CheckBreaker].result.OpenUntilUtc
```

---

### Recipe 8 — Ops monitoring process (scheduled, e.g. every 5 minutes)

Gives the operations team a live view of connection pool health and breaker states, without touching the device.

**Step 1 — Admin.PoolStatistics.GetStatistics**

No input required. Returns:

```
[PoolStats].result.TotalConnections        → active pooled connections across all devices
[PoolStats].result.ActiveConnections       → connections currently serving an operation
[PoolStats].result.IdleConnections         → connections sitting idle in the pool
[PoolStats].result.PerDevice               → array; each entry has Host, Port, UnitId, Connections, TotalOperations, TotalErrors, LastUsedUtc
```

**Step 2 — Admin.CircuitState.GetState** (repeat per device)

Run this step once per device you want to monitor.

```
[BreakerA].result.Exists        → false means the device has never been used
[BreakerA].result.State         → "Closed", "Open", or "HalfOpen"
[BreakerA].result.FailureCount  → consecutive failures toward threshold
[BreakerA].result.OpenUntilUtc  → when the Open window expires (null if Closed)
```

**Step 3 — conditional alert**

```
if [BreakerA].result.State ≠ "Closed" → send alert
```

---

### Recipe 9 — Manual breaker reset after device recovery

When a device that was offline comes back up, the breaker may still be Open (waiting for the cooldown). Reset it immediately without restarting the Agent.

**Step 1 — Admin.CircuitState.GetState** — confirm it is actually Open before resetting

**Step 2 — Admin.ResetCircuit.ResetState**

```json
{
  "Input": {
    "Host": "192.168.1.10",
    "Port": 502,
    "UnitId": 1
  }
}
```

The reset is audit-logged automatically. The next operation against the device will attempt a real connection.

---

## Error handling in Frends

### The pattern

```
[ReadStep].result.Success = false
    ↓
[ReadStep].result.Error.IsTransient = true   → transient: add a Frends retry connector
                                   = false  → permanent: alert and stop
```

### ErrorCategory quick reference

| Category | Transient? | What to do |
|----------|-----------|------------|
| `Timeout` | Yes | Retry; check `ConnectTimeoutMs` / `ReadTimeoutMs` |
| `ConnectionRefused` | Yes | Device may be rebooting; retry with backoff |
| `SlaveDeviceBusy` | Yes | Modbus code 6; retry after a short delay |
| `CircuitOpen` | Yes | Breaker tripped; check `CircuitState.GetState`, wait for cooldown or call `ResetCircuit.ResetState` after confirming device is back |
| `Backpressure` | Yes | Too many concurrent callers; increase `Options.Pool.MaxConnectionsPerDevice` or reduce Process concurrency |
| `ModbusException` | Depends | Check `Error.ModbusExceptionCode`; codes 1/2/3 are permanent config bugs; codes 5/6/10/11 are transient |
| `InvalidInput` | No | Fix the task configuration (wrong ValueType, NumberOfValues out of range, etc.) |
| `DecodingError` | No | Wrong `ByteOrder` or `ValueType` for this register — fix the options |
| `HostUnreachable` | No | Network routing problem |

### Enabling built-in retry

By default `Options.Retry.MaxAttempts = 1` (no retry). To enable automatic retry with exponential backoff, set it on the Options tab:

```json
"Options": {
  "Retry": {
    "MaxAttempts": 3,
    "InitialBackoffMs": 200,
    "BackoffStrategy": "ExponentialWithJitter"
  }
}
```

> **Write tasks and retry:** retrying a write re-sends the value to the device. Only enable `MaxAttempts > 1` on writes when the target register is idempotent (writing the same value twice has no additional effect).

---

## Common mistakes and fixes

| Symptom | Cause | Fix |
|---------|-------|-----|
| Write task throws `InvalidOperationException: … AllowWrites = false` | `Options.AllowWrites` is `false` — either hardcoded or the Frends Environment Variable it references (e.g. `#env.Modbus.AllowWrites`) is set to `false` | Set `Options.AllowWrites = true`, or set the Environment Variable to `true` in Environments → Variables |
| Values look like random bit noise | Wrong `ByteOrder` | Check device datasheet; try each of the four ByteOrder options against a known value — see [Byte-order troubleshooting](#byte-order-troubleshooting) |
| Read returns values that are off by 1 address | Wrong `AddressingMode` | Devices using Modicon notation (40001, 30001…) need `ModiconOneBased`; zero-based devices need `ZeroBased` |
| `ErrorCategory = CircuitOpen`, task returns immediately | Circuit breaker tripped after repeated device failures | Use `CircuitState.GetState` to see failure count and open-until time; use `ResetCircuit.ResetState` after device recovers |
| `ErrorCategory = Backpressure` | All pooled connections for this device are busy | Increase `Options.Pool.MaxConnectionsPerDevice` (only if device supports multiple concurrent connections) or reduce Process concurrency |
| All coil reads return `false` even when outputs are on | Reading `HoldingRegisters` instead of `Coils` | Set `DataType = Coils` |
| Float reads return `0` or a huge garbage number | `ValueType = Raw` (returns raw ushort) instead of `Float32` | Set `ValueType = Float32` and `NumberOfValues = 1` |
| Write appears to succeed but device register doesn't change | Mismatched `UnitId` — write went to a different slave | Verify the UnitId against the device configuration |
| Write task fails silently (Success=false) and Process continues | `ThrowOnFailure` was set to `false` | Either set `ThrowOnFailure = true` (the default) or add an explicit `Success` check step after every write |

---

## Admin tasks — when and why

These tasks are meant to be used in **Ops and monitoring Processes**, not in real-time data Processes. They read internal Agent state and do not communicate with Modbus devices (except `ResetCircuit`, which only resets in-memory state).

| Task | Use it when… |
|------|-------------|
| **PoolStatistics.GetStatistics** | Building an Ops dashboard; detecting connection leaks; confirming idle eviction is working; verifying how many devices are actively pooled |
| **CircuitState.GetState** | Checking why a Write or Read is returning `CircuitOpen`; monitoring device health in a scheduled alert process; verifying a device recovered after an outage |
| **ResetCircuit.ResetState** | After a known device maintenance window ends and you want traffic to resume immediately without waiting for the 30-second open window; every reset is audit-logged with the operator's context |

---

## Byte-order troubleshooting

Modbus carries multi-register values in one of four byte orderings. Use the device datasheet to confirm the notation, then pick the matching option:

| ByteOrder | ABCD notation | Registers on wire | Typical devices |
|-----------|--------------|-------------------|-----------------|
| `BigEndian` | ABCD | reg0=AB, reg1=CD | Most PLCs (default) |
| `LittleEndian` | DCBA | reg0=DC, reg1=BA | Some older controllers |
| `BigEndianByteSwap` | BADC | reg0=BA, reg1=DC | Some Modicon variants |
| `LittleEndianWordSwap` | CDAB | reg0=CD, reg1=AB | Schneider PM/iEM, WattNode |

**Quick test:** read a register that holds a known value (e.g., nominal voltage = 230.0). If the decoded number looks like garbage, cycle through the four ByteOrder options until it matches.

---

## Task reference

### Read.ReadData

Connects to a Modbus TCP slave, reads one block of registers or coils, decodes the values to the requested type, applies scale and offset, and returns a structured result.

#### Input parameters

| Name | Type | Default | Description |
|------|------|---------|-------------|
| Host | string | `"127.0.0.1"` | IP address or hostname of the Modbus slave |
| Port | int | `502` | TCP port |
| UnitId | byte | `1` | Modbus unit/slave ID (1–247) |
| DataType | ModbusDataType | `HoldingRegisters` | Coils, DiscreteInputs, HoldingRegisters, InputRegisters |
| StartAddress | ushort | `0` | First register/coil address |
| NumberOfValues | ushort | `1` | Number of values (not registers — Float32 counts as 1) |
| ValueType | ModbusValueType | `Raw` | Decoding type (hidden for coil data types) |

#### Options parameters

| Name | Type | Default | Description |
|------|------|---------|-------------|
| ByteOrder | ByteOrder | `BigEndian` | Register byte order (ABCD/DCBA/BADC/CDAB) |
| AddressingMode | AddressingMode | `ZeroBased` | ZeroBased or ModiconOneBased |
| Scale | double | `1.0` | Multiplier applied after decoding |
| Offset | double | `0.0` | Offset added after scaling |
| ConnectTimeoutMs | int | `5000` | TCP connect timeout in ms |
| ReadTimeoutMs | int | `5000` | Modbus read timeout in ms |
| ThrowOnFailure | bool | `false` | Throw exception instead of returning error Result |
| Retry.MaxAttempts | int | `1` | Set to 3+ to enable automatic retry with backoff |
| CircuitBreaker.Enabled | bool | `true` | Set to false to bypass the per-device circuit breaker |

#### Result properties

| Name | Type | Description |
|------|------|-------------|
| Success | bool | True if read succeeded |
| Data | object | Typed array: `ushort[]`, `short[]`, `int[]`, `float[]`, `double[]`, `bool[]`, `string` |
| FirstValue | object | First element of Data (convenience) |
| RawRegisters | ushort[] | Raw register words (null for coil types) |
| Error | ErrorDetail | Populated on failure: Category, IsTransient, Message, ModbusExceptionCode |
| Diagnostics | Diagnostics | ConnectTimeMs, ReadTimeMs, TotalTimeMs, WireStartAddress, WireRegisterCount |

#### ValueType reference

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

### ReadBatch.ReadBatchData

Opens one TCP connection and executes all items sequentially. A Modbus exception on one item fails that item but does not abort the batch. A socket-level failure aborts the entire batch.

#### BatchInput parameters

| Name | Type | Description |
|------|------|-------------|
| Host, Port, UnitId | — | Same as Read |
| Items | BatchReadItem[] | List of named reads |

#### BatchReadItem fields

| Name | Type | Description |
|------|------|-------------|
| Name | string | Key in the result Items dictionary |
| DataType | ModbusDataType | — |
| StartAddress | ushort | — |
| NumberOfValues | ushort | — |
| ValueType | ModbusValueType | — |
| Scale | double | Per-item scale (default 1.0) |
| Offset | double | Per-item offset (default 0.0) |

#### BatchResult properties

| Name | Type | Description |
|------|------|-------------|
| Success | bool | False only on socket-level failure |
| Items | Dictionary<string, ReadOutcome> | Per-item results (Success, FirstValue, Data, Error) |
| Diagnostics | Diagnostics | Timing for the entire batch |
| Error | ErrorDetail | Set only on socket-level failure |

---

### Write tasks

All Write tasks share the same base Input fields:

| Name | Type | Default | Description |
|------|------|---------|-------------|
| Host | string | `"127.0.0.1"` | Modbus slave IP or hostname |
| Port | int | `502` | TCP port |
| UnitId | byte | `1` | Modbus unit/slave ID |
| DataType | ModbusDataType | `HoldingRegisters` | HoldingRegisters or Coils (task-dependent) |
| StartAddress | ushort | `0` | First register/coil address |
| ValueType | ModbusValueType | `Raw` | Encoding type |
| Value | object | — | **WriteSingleRegister** / **WriteSingleCoil**: single value to write (`object` or `bool`) |
| Values | object[] | — | **WriteMultiple** / **WriteBatch**: array of values to write |

Write tasks use `WriteOptions`, which extends the read `Options` with two changed defaults:

| Option | Write default | Why |
|--------|--------------|-----|
| ThrowOnFailure | `true` | Silent write failures to control systems are hazardous |
| Retry.MaxAttempts | `1` | Retrying a non-idempotent write re-applies its effect |

#### WriteResult properties

| Name | Type | Description |
|------|------|-------------|
| Success | bool | True if write succeeded |
| WireRegistersWritten | ushort | Number of 16-bit registers written on the wire |
| Error | ErrorDetail | Populated on failure |
| Diagnostics | Diagnostics | ConnectTimeMs, ReadTimeMs, TotalTimeMs |

#### WriteSingleCoil — FC05

Writes one boolean value to one coil via `Input.Value` (a single `bool`).

#### WriteSingleRegister — FC06

Writes one value to one register. `ValueType` must be `Raw`, `UInt16`, or `Int16`. Use `WriteMultiple` for multi-register types such as `Float32`.

#### WriteMultiple — FC15 / FC16

Writes a contiguous block of coils (FC15) or registers (FC16). `DataType` selects which function code is used.

#### ReadWriteMultiple — FC23

Reads one register block and writes another in a single atomic transaction. Input has two address fields: `ReadStartAddress` + `ReadRegisterCount` for the read portion, and `WriteStartAddress` + `WriteRegisters` (ushort[]) for the write portion. The result includes `ReadRegisters` (ushort[]) alongside the standard `WriteResult` fields.

#### WriteBatch

Executes multiple write items over one TCP connection. Each item specifies its own `Name`, `DataType`, `StartAddress`, `ValueType`, and `Values`. A Modbus exception on one item fails that item but does not abort remaining items. Socket-level failure aborts the entire batch.

Result: `WriteBatchResult.Items` is a `Dictionary<string, WriteOutcome>` keyed by item `Name`.

---

### Admin tasks

Admin tasks read Agent-internal state. They never open Modbus connections to devices (except for the key lookup in `CircuitState` and `ResetCircuit`, which is in-memory only).

#### PoolStatistics.GetStatistics

No input. Returns a snapshot of the connection pool:

| Field | Description |
|-------|-------------|
| TotalConnections | Active pooled connections across all devices |
| ActiveConnections | Connections currently serving an operation |
| IdleConnections | Connections sitting idle in the pool |
| PerDevice | Per-device breakdown array — each entry: Host, Port, UnitId, Connections, TotalOperations, TotalErrors, LastUsedUtc |

#### CircuitState.GetState

Input: `Host`, `Port`, `UnitId`. Returns:

| Field | Description |
|-------|-------------|
| Exists | False if no operations have ever been attempted against this device |
| State | `"Closed"`, `"Open"`, or `"HalfOpen"` |
| FailureCount | Consecutive failures counted toward the open threshold |
| LastFailureUtc | Timestamp of the most recent counted failure |
| OpenUntilUtc | When the Open window expires; null when State is not Open |

#### ResetCircuit.ResetState

Input: `Host`, `Port`, `UnitId`. Resets the breaker to Closed, zeroes the failure counter, and emits an audit event. The next operation will attempt a real Modbus connection.

---

## Building and testing

```bash
git clone https://github.com/qconn-io/Frends.Qconn.ModbusTcp.git
cd Frends.Qconn.ModbusTcp
dotnet build
dotnet test
dotnet pack --configuration Release
```

Public GitHub releases are created from version tags. After updating `CHANGELOG.md` and confirming `<Version>` in `Frends.Qconn.ModbusTcp.csproj`, push a tag such as `v2.0.7` to publish the package artifacts to the [Releases page](https://github.com/qconn-io/Frends.Qconn.ModbusTcp/releases).

See [CHANGELOG.md](./CHANGELOG.md) for the full version history and [SECURITY.md](./SECURITY.md) for the Write threat model and TLS deployment guidance.
