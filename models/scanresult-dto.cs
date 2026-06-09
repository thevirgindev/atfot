using System;
using System.Collections.Generic;

namespace pewbot.models;

public class ScanResultDto
{
    public string TargetLookup { get; set; } = string.Empty;
    public string ModuleSource { get; set; } = string.Empty;
    public string ScanTimestamp { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
    public string? RawApiResponse { get; set; }
    public string? Summary { get; set; }
    public List<string> DeepLinks { get; set; } = new();
    public Dictionary<string, string> ExtractedData { get; set; } = new();
}