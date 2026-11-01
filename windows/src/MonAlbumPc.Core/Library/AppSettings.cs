using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonAlbumPc.Core.Library;

public enum AppMode
{
    /// <summary>The PC receives the photos itself: today's behaviour, for people without a NAS.</summary>
    Autonomous,

    /// <summary>The PC only reads a library written by a NAS: no server, no port, no pairing, no import.</summary>
    Viewer,
}

/// <summary>What this PC does, kept in <c>settings.json</c> at the root of the data folder. Absent means autonomous.</summary>
public sealed record AppSettings(AppMode Mode = AppMode.Autonomous, string? LibraryRoot = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [JsonIgnore]
    public bool IsViewer => Mode == AppMode.Viewer;

    public static string FilePath(string dataRoot) => Path.Combine(dataRoot, "settings.json");

    /// <summary>The saved settings. A missing or damaged file, or a viewer without library, gives the autonomous mode.</summary>
    public static AppSettings Load(string dataRoot)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath(dataRoot)), Json);
            return settings is { IsViewer: true, LibraryRoot: null or "" } or null ? new AppSettings() : settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        var target = FilePath(dataRoot);
        var temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, Json), new UTF8Encoding(false));
        File.Move(temporary, target, overwrite: true);
    }

    /// <summary>The viewer has its own catalog, so the autonomous one stays intact and the user can switch back.</summary>
    public string CatalogDirectory(string dataRoot) => IsViewer ? Path.Combine(dataRoot, "viewer") : dataRoot;
}
