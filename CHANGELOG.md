# Changelog

## [2.0.6] - 2026-04-21

### Fixed

- **README**: `PoolStatistics.GetStatistics` result property names corrected in Recipe 8 and the Task Reference table. `ConnectionsPerDevice` → `PerDevice`; `ConnectionCount` (per-device) → `Connections`; `IdleSinceUtc` (per-device) → `LastUsedUtc`. `TotalOperations` and `TotalErrors` are per-device fields on each `PerDevice[]` entry, not top-level properties. Added the missing top-level `ActiveConnections` and `IdleConnections` fields to both locations.
- **Diagram** (Diagram 1 — Product Overview): WriteGuard safety note previously described a non-existent environment variable `ModbusWritesAllowed`. Corrected to describe the actual mechanism: `Options.AllowWrites` is a task parameter set to a Frends Environment Variable (e.g. `#env.Modbus.AllowWrites`) in the Process editor.

## [2.0.0] - 2026-04-20

### Added

- **Write Tasks**: `Frends.Qconn.ModbusTcp.Write.WriteSingleCoil`, `WriteSingleRegister`, `WriteMultiple`, `ReadWriteMultiple`, and `WriteBatch`. All writes are gated by `Options.AllowWrites` (see SECURITY.md); the recommended pattern is to set it via the `#env.Modbus.AllowWrites` Frends Environment Variable so that setting it to `false` blocks every write at Task entry before any TCP socket is opened.
- **Connection pool**: Transparent Agent-wide TCP connection pool keyed by (Host, Port, UnitId, TransportMode, TLS fingerprint). Reused across `Read`, `ReadBatch`, and all Write Tasks. Configured via `Options.Pool`. Agent-wide caps via `ModbusTcp.MaxTotalConnections` (default 200) and `ModbusTcp.MaxConnectionsPerDevice` (default 1). Background idle eviction every 30s.
- **Circuit breaker**: Per-device three-state (Closed / Open / HalfOpen) breaker. Configured via `Options.CircuitBreaker`. Enabled by default with a 5-consecutive-failure threshold and a 30-second Open duration. Modbus exception codes 1/2/3 (client bugs) do not count as failures.
- **Retry**: Per-operation retry with exponential-with-jitter backoff. Configured via `Options.Retry`. **Default `MaxAttempts = 1`** preserves v1 single-shot behavior; set to 3+ to enable retry of transient failures. Classification via `Options.Retry.RetryOn` bit mask.
- **Audit logging**: Every operation (read / write / admin) emits a structured `ModbusAuditEvent`. Sink selection via `ModbusTcp.AuditSink` Frends Environment Variable: `FrendsLog` (default, writes to Process Instance log via Trace), `File` (JSON-lines, daily rotated), `Syslog` (RFC 5424 UDP), `OpenTelemetry`, `None`. Audit events are never redacted by Agent-level `DoNotLog` flags.
- **OpenTelemetry primitives**: Static `Meter("Frends.Qconn.ModbusTcp", "2.0")` with counters `modbus.operations`, `modbus.errors`, `modbus.retry.count`, `modbus.backpressure.rejected`, `modbus.connection.evicted`, and histogram `modbus.duration`. Static `ActivitySource` for distributed tracing. No hard dependency on the OpenTelemetry SDK.
- **Admin Tasks**: `Frends.Qconn.ModbusTcp.Admin.PoolStatistics`, `CircuitState`, `ResetCircuit` — introspection and manual control for building Ops dashboards as Frends Processes.
- **ErrorCategory additions**: `CircuitOpen`, `Backpressure`, `SocketError`, `GatewayTimeout`, `SlaveBusy`, `TlsError`. Existing v1 members retain their values and order.
- **Diagnostics additions** (init-only; invisible to v1 constructor callers): `AttemptHistory`, `TlsProtocol`, `TlsCipherSuite`, `ServerCertificateThumbprint`, `ServerCertificateSubject`, `ServerCertificateExpiresUtc`, `ModbusRole`.

### Fixed

- `Diagnostics.ConnectTimeMs` now reports the elapsed connect attempt duration on TCP timeouts (previously reported 0).

### Preserved (no breaking changes)

- All v1 `Input`, `Options` (fields 1–7), `Result`, `Diagnostics`, `ErrorDetail`, `BatchInput`, `BatchReadItem`, `BatchResult`, `ReadOutcome` public shapes unchanged.
- All v1 public Task signatures unchanged: `Read.ReadData(Input, Options, CancellationToken)`, `ReadBatch.ReadBatchData(BatchInput, Options, CancellationToken)`.
- All v1 `ErrorCategory` members retain their original ordinal values.
- All v1 tests pass unmodified.

### Not in this milestone (reserved for v2.1+)

- TLS (`TlsOptions`, `TlsChannelFactory`, `TransportSecurity.Tls` / `MutualTls`).
- Device Profiles library and `LoadRegisterMap` CSV parser.
- Server mode (Modbus slave hosting).
- RTU-over-TCP transport (`TransportMode.RtuOverTcp` throws `NotSupportedException` today).
- Cross-Agent breaker state mirroring via Shared State (`Options.Pool.ShareCircuitStateAcrossAgents` accepted but no-op with a WARN log).

## [1.0.0] - 2026-04-19

### Added

- Initial implementation
