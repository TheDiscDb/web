using System;

namespace TheDiscDb.Web.Data;

public class ApiKeyUsageLog
{
    public long Id { get; set; }
    public int ApiKeyId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? OperationName { get; set; }
    public double FieldCost { get; set; }
    public double TypeCost { get; set; }
    public int DurationMs { get; set; }

    public ApiKey ApiKey { get; set; } = null!;
}
