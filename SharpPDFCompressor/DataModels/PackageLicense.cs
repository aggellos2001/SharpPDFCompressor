using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace SharpPDFCompressor.DataModels;

public class PackageLicense
{
    [JsonPropertyName("PackageId")] public string? PackageId { get; set; }

    [JsonPropertyName("PackageVersion")] public string? PackageVersion { get; set; }

    [JsonPropertyName("PackageProjectUrl")]
    public string? PackageProjectUrl { get; set; }

    [JsonPropertyName("License")] public string? License { get; set; }

    [JsonPropertyName("LicenseUrl")] public string? LicenseUrl { get; set; }

    // This is the new property we will bind to the UI
    [JsonIgnore]
    public string DisplayLicense
    {
        get
        {
            if (string.IsNullOrWhiteSpace(License)) return "Unknown License";

            // 1. If it's already a short identifier like "MIT"
            if (License.Length < 50 && !License.Contains('\n'))
                return License;

            // 2. If the tool output a URL instead of text
            if (License.StartsWith("http"))
                return "Custom (See URL)";

            // 3. If it is a giant block of text, grab the first non-empty line
            string? firstLine = License.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            // Trim it with an ellipsis if that first line is still too long
            if (firstLine is { Length: > 50 })
            {
                return firstLine[..47] + "...";
            }

            return firstLine ?? "Custom License";
        }
    }
    [JsonIgnore]
    public string? ActionableUrl
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LicenseUrl) ||
                LicenseUrl.Contains("deprecateLicenseUrl", StringComparison.OrdinalIgnoreCase))
            {
                return PackageProjectUrl;
            }
            return LicenseUrl;
        }
    }
}