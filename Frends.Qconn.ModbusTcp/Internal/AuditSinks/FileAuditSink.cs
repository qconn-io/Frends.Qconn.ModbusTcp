using System;
using System.Diagnostics;
using System.IO;
using Frends.Qconn.ModbusTcp.Common.Definitions;
using Newtonsoft.Json;

namespace Frends.Qconn.ModbusTcp.Internal.AuditSinks;

/// <summary>Writes audit events as JSON-lines to a file, rotated daily. Enable with ModbusTcp.AuditSink=File
/// and ModbusTcp.AuditFilePath=/var/log/frends/modbus-audit.jsonl (path is the file stem; today's date is appended).</summary>
internal sealed class FileAuditSink : IAuditSink
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        NullValueHandling = NullValueHandling.Include,
    };

    private readonly string basePath;
    private readonly object gate = new();

    public FileAuditSink(string basePath)
    {
        this.basePath = basePath;
    }

    public void Emit(ModbusAuditEvent evt)
    {
        try
        {
            var day = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
            var path = $"{basePath}.{day}";
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonConvert.SerializeObject(evt, SerializerSettings);
            lock (gate)
            {
                File.AppendAllText(path, json + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[modbus.audit.error] file sink failed: {ex.Message}");
        }
    }
}
