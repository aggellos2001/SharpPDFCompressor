using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.ApplicationModel;
using Windows.Storage;

namespace SharpPDFCompressor.DataModels;

[JsonConverter(typeof(JsonStringEnumConverter<ThemePreference>))]
internal enum ThemePreference
{
    System,
    Light,
    Dark
}

[JsonConverter(typeof(JsonStringEnumConverter<LanguagePreference>))]
internal enum LanguagePreference
{
    System,
    English,
    Greek
}

internal sealed class AppSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public LanguagePreference Language { get; set; } = LanguagePreference.System;

    [JsonIgnore]
    public LanguagePreference ResolvedLanguage => this.Language switch
    {
        LanguagePreference.Greek => LanguagePreference.Greek,
        LanguagePreference.English => LanguagePreference.English,
        _ => CultureInfo.CurrentUICulture.Name.StartsWith("el", StringComparison.OrdinalIgnoreCase)
            ? LanguagePreference.Greek
            : LanguagePreference.English
    };
}

internal static class AppSettingsStore
{
    private const string SettingsKey = "SettingsJson";
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SharpPDFCompressor");
    private static readonly string SettingsPath = Path.Combine(AppDataFolder, "settings.json");

    public static string AppDataFolderPath => IsPackaged
        ? ApplicationData.Current.LocalFolder.Path
        : AppDataFolder;

    public static void EnsureAppDataFolderExists()
    {
        if (!IsPackaged)
        {
            Directory.CreateDirectory(AppDataFolder);
        }
    }

    private static bool IsPackaged
    {
        get
        {
            try
            {
                _ = Package.Current;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public static AppSettings Load()
    {
        try
        {
            string? json = IsPackaged
                ? ApplicationData.Current.LocalSettings.Values[SettingsKey] as string
                : File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null;
            if (json is not null)
            {
                AppSettings? settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
                if (settings is not null && IsValid(settings))
                {
                    return settings;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or JsonException or NotSupportedException or ArgumentException)
        {
        }

        AppSettings defaults = new();
        Save(defaults);
        return defaults;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
            if (IsPackaged)
            {
                ApplicationData.Current.LocalSettings.Values[SettingsKey] = json;
                return;
            }

            string? directory = Path.GetDirectoryName(SettingsPath);
            if (directory is null)
            {
                return;
            }

            Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or JsonException or NotSupportedException or ArgumentException)
        {
        }
    }

    private static bool IsValid(AppSettings settings) =>
        Enum.IsDefined(settings.Theme) &&
        Enum.IsDefined(settings.Language);
}