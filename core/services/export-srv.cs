using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using atfot.models;
using System.Collections.Generic;
using System.Linq;

namespace atfot.core.services;

public class ExportService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public MemoryStream BuildTextStream(ScanResultDto dto, string? aiSummary = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=====================================================================");
        sb.AppendLine($"OSINT ANALYST LOG // MODULE: {(dto.ModuleSource ?? "unknown").ToUpper()}");
        sb.AppendLine($"TIMESTAMP: {dto.ScanTimestamp ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")} // TARGET: {dto.TargetLookup ?? "unknown"}");
        sb.AppendLine("=====================================================================");
        sb.AppendLine();

        sb.AppendLine("[RAW API RESPONSE]");
        if (!string.IsNullOrEmpty(dto.RawApiResponse))
        {
            try
            {
                var parsed = JToken.Parse(dto.RawApiResponse!);
                sb.AppendLine(parsed.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch
            {
                sb.AppendLine(dto.RawApiResponse);
            }
        }
        else
        {
            sb.AppendLine("No raw response captured.");
        }
        sb.AppendLine();

        sb.AppendLine("[EXTRACTED SUMMARY]");
        sb.AppendLine(dto.Summary ?? "No summary generated.");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(aiSummary))
        {
            sb.AppendLine("[AI GENERATED SUMMARY]");
            sb.AppendLine(aiSummary);
            sb.AppendLine();
        }

        sb.AppendLine("[GENERATED INVESTIGATIVE LINKS]");
        if (dto.DeepLinks != null && dto.DeepLinks.Count > 0)
        {
            foreach (var link in dto.DeepLinks)
                sb.AppendLine($"- {link}");
        }
        else
        {
            sb.AppendLine("No links generated.");
        }

        sb.AppendLine();
        sb.AppendLine("[EXTRACTED DATA FIELDS]");
        if (dto.ExtractedData != null && dto.ExtractedData.Count > 0)
        {
            foreach (var (key, value) in dto.ExtractedData)
                sb.AppendLine($"{key.PadRight(15)} : {value}");
        }
        else
        {
            sb.AppendLine("No extracted data fields available.");
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public MemoryStream BuildJsonStream(ScanResultDto dto, string? aiSummary = null)
    {
        object? rawJsonObj = null;
        
        if (!string.IsNullOrEmpty(dto.RawApiResponse))
        {
            try
            {
                rawJsonObj = JsonSerializer.Deserialize<JsonElement>(dto.RawApiResponse);
            }
            catch
            {
                rawJsonObj = dto.RawApiResponse;
            }
        }

        var exportObj = new
        {
            dto.TargetLookup,
            dto.ModuleSource,
            dto.ScanTimestamp,
            RawApiResponse = rawJsonObj,
            dto.Summary,
            aiSummary,
            DeepLinks = dto.DeepLinks ?? new List<string>(),
            ExtractedData = dto.ExtractedData ?? new Dictionary<string, string>()
        };

        var json = JsonSerializer.Serialize(exportObj, _jsonOptions);
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    public MemoryStream BuildCsvStream(ScanResultDto dto)
    {
        var fields = new Dictionary<string, string>();

        fields["Target"] = dto.TargetLookup ?? "";
        fields["Module"] = dto.ModuleSource ?? "";
        fields["Timestamp"] = dto.ScanTimestamp ?? "";

        if (dto.ExtractedData != null && dto.ExtractedData.Count > 0)
        {
            foreach (var kv in dto.ExtractedData)
                fields[kv.Key] = kv.Value;
        }
        else if (!string.IsNullOrEmpty(dto.RawApiResponse))
        {
            try
            {
                var json = JToken.Parse(dto.RawApiResponse);
                if (json["data"] != null) json = json["data"];
                
                if (json != null)
                {
                    foreach (var prop in json.Children<JProperty>())
                    {
                        var value = prop.Value.ToString();
                        if (string.IsNullOrEmpty(value)) continue;

                        string cleanKey = prop.Name switch
                        {
                            "full_name" => "Full Name",
                            "followers_count" => "Followers",
                            "following_count" => "Following",
                            "media_count" => "Has Posts",
                            "is_private" => "isPrivate",
                            "is_verified" => "isVerified",
                            "is_business_account" => "Business Account",
                            _ => prop.Name
                        };

                        if (prop.Name == "account_type")
                        {
                            cleanKey = "Account Type";
                            value = value == "1" ? "Personal" : "Business";
                        }
                        else if (value.ToLower() == "true") value = "Yes";
                        else if (value.ToLower() == "false") value = "No";

                        fields[cleanKey] = value;
                    }
                }
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(dto.Summary))
            fields["Summary"] = dto.Summary.Replace("\n", " ");

        var headers = fields.Keys.ToList();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(h => EscapeCsv(h))));
        sb.AppendLine(string.Join(",", headers.Select(h => EscapeCsv(fields[h]))));
        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static string EscapeCsv(string? field)
    {
        if (string.IsNullOrEmpty(field))
            return "";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }
}
