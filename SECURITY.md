# SECURITY — Frends.Qconn.ModbusTcp v2.0 (Milestone 1)

Modbus TCP is a plaintext protocol with no built-in authentication. This package ships defensive defaults and opt-in safety controls. This document covers the threat model, the controls shipped in v2.0 Milestone 1, and the items explicitly reserved for the next milestone.

## Threat model

- **Accidental or malicious writes from a Frends Process**: any Process with access to a Modbus-capable Environment can trigger a Write Task against a configured device. Writes to control registers can change physical plant state.
- **Retry amplification**: a misconfigured retry policy can multiply a single intent into many wire requests, potentially applying non-idempotent writes repeatedly.
- **Plaintext eavesdropping and tampering**: standard Modbus TCP carries no confidentiality or integrity protection on the wire. An attacker on the path can observe reads/writes and inject spoofed responses.
- **Connection storms against failing devices**: a dead or slow device served by N Processes at 1 Hz becomes 60 × N connection attempts per minute, each burning socket and scheduler resources on the Agent.
- **Sensitive values in logs**: register values may represent process data that Process authors do not want in Agent logs.

## Controls in v2.0 Milestone 1

### `WriteOptions.AllowWrites` — Write gate (`WriteGuard`)

Every Write Task checks `Options.AllowWrites` as its very first step, before any socket is opened. When `AllowWrites` is `false`, the Task throws `InvalidOperationException` with the message: *"Modbus writes are disabled (Options.AllowWrites = false)…"* No TCP connection is attempted.

`AllowWrites` is a standard Frends task option. The recommended pattern for per-Environment control:

1. In **Frends Management portal → Environments → [Environment] → Variables**, create a variable. Both Group Name and Variable Name are required fields. Example: Group `Modbus`, Variable `AllowWrites`, value `true` or `false`.
2. In each Write Task's **Options tab**, set **Allow Writes** to `#env.Modbus.AllowWrites` (replacing `Modbus` with your chosen Group Name).

The Frends runtime resolves `#env.Modbus.AllowWrites` to the Environment Variable value at execution time, so the same Process definition behaves differently on read-only vs. control-plane Environments without any code change.

**Recommended Production configuration**: set `#env.Modbus.AllowWrites` to `false` (via Environments → Variables, Group `Modbus`, Variable `AllowWrites`) on every Environment that is not specifically authorized to write. Enable (`true`) only on dedicated control-plane Environments. The default when no value is provided in Options is `true`, preserving v1 backward compatibility; Production deployments should configure the variable explicitly.

### Write default behaviors

- `Options.ThrowOnFailure` defaults to `true` for writes (the subclass `WriteOptions` overrides the v1 read default). Silent write failures to control systems are hazardous; opt out explicitly only if the caller can reason about partial success.
- `Options.Retry.MaxAttempts` defaults to `1` for writes. Retrying a non-idempotent write re-applies its effect. A Process that wants retry must set `MaxAttempts > 1` explicitly, and should verify idempotency at the target register.

### Audit logging

Every read/write/admin operation emits a structured `ModbusAuditEvent` via `AuditRouter` including `Operation`, `Host`, `Port`, `UnitId`, `FunctionCode`, `StartAddress`, `Count`, `Success`, `ErrorCategory`, `ModbusExceptionCode`, `AttemptCount`, `TotalTimeMs`, and for writes `ValuesWritten`. Sinks: `FrendsLog` (default), `File`, `Syslog`, `OpenTelemetry`, `None`. Audit events are not redacted by Agent-level `DoNotLog` flags.

### Circuit breaker

Per-device breaker rate-limits requests to failing devices. Modbus exception codes 1/2/3 (client bugs) do not count; device-side codes 4/5/6/10/11 do. Default threshold 5 consecutive failures; default Open duration 30s; auto-recovery via HalfOpen probes.

### Connection pool

Agent-wide per-device pool with per-connection semaphore enforcing serial req/resp semantics. Caps via `ModbusTcp.MaxTotalConnections` (default 200) and `ModbusTcp.MaxConnectionsPerDevice` (default 1). Idle eviction after 60s.

## Reserved for v2.1+ — NOT in this milestone

### TLS

- `Options.Tls` block and `TransportSecurity.Tls`/`MutualTls` modes are **not wired**. `TransportSecurity` is declared so the connection pool key is stable across versions, but selecting a non-`None` value currently throws `NotSupportedException` at connect time.
- **Mitigation for v2.0 deployments requiring transport protection**: terminate TLS at a sidecar (stunnel, envoy) on both endpoints. Configure the Agent and device to connect through the sidecar on localhost; the sidecar handles certificate validation and encryption. See your Environment-specific runbooks.

### Server-side TLS

`Server/*` Tasks are not shipped in v2.0 Milestone 1. Modbus slave hosting (with or without TLS) is deferred.

### Cross-Agent circuit-breaker state sharing

`Options.Pool.ShareCircuitStateAcrossAgents` is accepted but no-op in v2.0 Milestone 1. Setting it to `true` logs a WARN and falls back to local-only breaker state. A failing device will be tracked independently per Agent.

## Known limitations

- **Plaintext wire traffic** — Modbus TCP without TLS carries no confidentiality or integrity. Treat Modbus networks as physically isolated trust zones until TLS support ships.
- **Per-connection serialization** — one in-flight request per pooled connection per device. Concurrent callers to the same device serialize behind the connection's semaphore. Raise `ModbusTcp.MaxConnectionsPerDevice` only if the target device accepts multiple simultaneous connections.
- **Retry is off by default** for both reads and writes in this milestone. Opt in with `Options.Retry.MaxAttempts`.
- **Audit `ValuesWritten` is not redacted.** If register values themselves are sensitive, set the Frends Environment Variable `ModbusTcp.AuditSink=File` with a restricted-access path, or `Syslog` pointing at a dedicated SIEM.

## Reporting security issues

Report suspected vulnerabilities in this Task privately to the Frends security team per the Frends security-disclosure policy rather than opening a public issue.
