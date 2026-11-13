using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Degausser;

public enum StartupViewPreference
{
    LastUsed,
    NowPlaying,
    ThreeDsContent
}

public enum MainViewPreference
{
    NowPlaying,
    ThreeDsContent
}

public enum SoundFontMappingMode
{
    GeneralMidi,
    BbpFaithful
}

public sealed class UserSettings
{
    public string LibraryPath { get; set; } = UserSettingsStore.GetDefaultLibraryPath();
    public StartupViewPreference StartupView { get; set; } = StartupViewPreference.LastUsed;
    public MainViewPreference LastView { get; set; } = MainViewPreference.NowPlaying;
    /// <summary>Author given to a converted BDX that names no contributor.</summary>
    public string BdxDefaultAuthor { get; set; } = BdxToBbpConverter.DefaultAuthor;
    public BdxConversionOptions BdxOptions => new(DefaultAuthor: BdxDefaultAuthor);
    public bool RememberWindowPlacement { get; set; } = true;
    public int IntensityEffectPercent { get; set; } = 100; // legacy JSON setting; intentionally not used
    public string MidiTextEncoding { get; set; } = "utf-8";
    public string MidiProfilePath { get; set; } = string.Empty; // optional BBP2MIDI v1/v2 JSON
    public string SoundFontPath { get; set; } = UserSettingsStore.GetDefaultSoundFontPath();
    /// <summary>Folder of scorefix .txt files; empty for none. Optional: Degausser works without them.</summary>
    public string ScorefixPath { get; set; } = UserSettingsStore.GetDefaultScorefixPath();
    public SoundFontMappingMode SoundFontMapping { get; set; } = SoundFontMappingMode.BbpFaithful;
    public double WindowWidth { get; set; } = 1440;
    public double WindowHeight { get; set; } = 900;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool WindowMaximized { get; set; }
}

/// <summary>
/// Small writable preference store. Nothing is written unless the user turns on
/// "remember settings"; then the file lives under LocalAppData, rather than beside
/// the executable, so portable/self-contained releases can remain read-only and
/// updates do not overwrite preferences.
/// </summary>
public static class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Degausser");

    public static string SettingsPath { get; } = Path.Combine(SettingsDirectory, "settings.json");

    public static UserSettings Current { get; private set; } = new();

    /// <summary>
    /// Whether settings are kept in <see cref="SettingsPath"/>. Off until the user asks for it, so a
    /// fresh copy of the app leaves nothing behind; an existing settings file counts as having asked.
    /// </summary>
    public static bool RememberSettings { get; private set; }

    public static void Initialize()
    {
        RememberSettings = File.Exists(SettingsPath);
        Current = Load();
    }

    public static void SetRememberSettings(bool remember)
    {
        RememberSettings = remember;
        if (remember)
            Save();
        else
            try { File.Delete(SettingsPath); } catch { }
    }

    public static void Save()
    {
        if (!RememberSettings) return;
        Directory.CreateDirectory(SettingsDirectory);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Current, JsonOptions));
        File.Move(temporary, SettingsPath, overwrite: true);
    }

    public static string GetManagedSoundFontPath()
        => Path.Combine(SettingsDirectory, "SoundFonts", "BBP.sf2");

    public static string GetDefaultSoundFontPath()
    {
        string besideExecutable = Path.Combine(AppContext.BaseDirectory, "BBP.sf2");
        if (File.Exists(besideExecutable)) return besideExecutable;
        string managed = GetManagedSoundFontPath();
        return File.Exists(managed) ? managed : string.Empty;
    }

    /// <summary>A "scorefixes" folder beside the executable, if the release was unpacked with one.</summary>
    public static string GetDefaultScorefixPath()
    {
        string besideExecutable = Path.Combine(AppContext.BaseDirectory, "scorefixes");
        return Directory.Exists(besideExecutable) ? besideExecutable : string.Empty;
    }

    /// <summary>Reads the scorefix folder from the settings into <see cref="ScorefixLibrary.Current"/>.</summary>
    public static ScorefixLibrary LoadScorefixes()
    {
        ScorefixLibrary library = ScorefixLibrary.FromDirectory(Current.ScorefixPath);
        ScorefixLibrary.Current = library;
        if (library.Directory is not null)
            Logging.Log($"{library.Count} scorefix(es) loaded from {library.Directory}.");
        foreach (string problem in library.Problems)
            Logging.Log($"Scorefix skipped: {problem}");
        return library;
    }

    public static string GetDefaultLibraryPath()
    {
        // Preserve the old v2/v3-preview configuration when upgrading in-place.
        string legacyConfig = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(legacyConfig))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(legacyConfig));
                if (document.RootElement.TryGetProperty("Library", out JsonElement library)
                    && library.TryGetProperty("StartupDirectory", out JsonElement value))
                {
                    string? path = value.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                        return path;
                }
            }
            catch
            {
                // A malformed legacy config should never prevent startup.
            }
        }

        if (Directory.Exists(@"C:\Daigasso"))
            return @"C:\Daigasso";

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
            documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(documents))
            documents = AppContext.BaseDirectory;

        return Path.Combine(documents, "Degausser Library");
    }

    private static UserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new UserSettings();

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        }
        catch (Exception exception)
        {
            Logging.Log($"Could not read user settings; defaults will be used: {exception.Message}");
            return new UserSettings();
        }
    }
}
