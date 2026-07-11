using System;
using System.IO;
using System.Text.Json;
using LiteDB;

namespace MouseKeyProxy.Common;

/// <summary>
/// FR-MKP-022 / TR-MKP-CFG-001: durable non-secret appliance configuration
/// (gadget function defaults, media paths, share/SMB flags).
/// Stored in LiteDB under <c>/etc/mkp/config.db</c> on the Pi.
/// </summary>
public sealed record ApplianceConfig
{
    /// <summary>LiteDB document id (singleton row).</summary>
    public int Id { get; init; } = 1;

    /// <summary>Keyboard HID enabled by default / last applied.</summary>
    public bool KeyboardEnabled { get; init; } = true;

    /// <summary>Mouse HID enabled by default / last applied.</summary>
    public bool MouseEnabled { get; init; } = true;

    /// <summary>Disk FS LUN enabled.</summary>
    public bool FsEnabled { get; init; } = true;

    /// <summary>Disk FS access mode.</summary>
    public DeviceFsAccess FsAccess { get; init; } = DeviceFsAccess.ReadOnly;

    /// <summary>CD-ROM LUN enabled.</summary>
    public bool CdromEnabled { get; init; }

    /// <summary>CD media source.</summary>
    public DeviceMediaSource CdromMediaSource { get; init; } = DeviceMediaSource.Device;

    /// <summary>CD media path or name under deploy media root.</summary>
    public string? CdromMediaPath { get; init; }

    /// <summary>Virtual floppy LUN enabled.</summary>
    public bool FloppyEnabled { get; init; }

    /// <summary>Floppy media source.</summary>
    public DeviceMediaSource FloppyMediaSource { get; init; } = DeviceMediaSource.Device;

    /// <summary>Floppy media path or name.</summary>
    public string? FloppyMediaPath { get; init; }

    /// <summary>gRPC folder share enabled.</summary>
    public bool FolderShareEnabled { get; init; } = true;

    /// <summary>SMB share enabled.</summary>
    public bool SmbEnabled { get; init; } = true;

    /// <summary>Share display name (gRPC + SMB).</summary>
    public string ShareName { get; init; } = "MouseKeyProxy";

    /// <summary>Mount path for the FAT32 deploy partition (e.g. /mnt/mkp-deploy).</summary>
    public string DeployMountPath { get; init; } = "/mnt/mkp-deploy";
}

/// <summary>
/// FR-MKP-022: abstraction over durable appliance configuration persistence.
/// </summary>
public interface IApplianceConfigStore : IDisposable
{
    /// <summary>Loads the current config (defaults if empty).</summary>
    ApplianceConfig Get();

    /// <summary>Persists the full config document.</summary>
    /// <param name="config">Config to save.</param>
    void Save(ApplianceConfig config);

    /// <summary>
    /// Imports <paramref name="seedJsonPath"/> only when no config document exists yet.
    /// Returns true when seed was applied.
    /// </summary>
    /// <param name="seedJsonPath">Path to seed.json written at first boot.</param>
    bool TryImportSeed(string seedJsonPath);

    /// <summary>Absolute path of the LiteDB file.</summary>
    string DatabasePath { get; }
}

/// <summary>
/// LiteDB-backed <see cref="IApplianceConfigStore"/>. Default production path:
/// <c>/etc/mkp/config.db</c> (directory mode 0700, root-owned on Linux).
/// </summary>
public sealed class LiteDbApplianceConfigStore : IApplianceConfigStore
{
    private const string CollectionName = "appliance_config";
    private static readonly JsonSerializerOptions SeedJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly object _gate = new();
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<ApplianceConfig> _collection;

    /// <summary>Creates a store at the given database file path (parent dirs created).</summary>
    /// <param name="databasePath">Full path to config.db (e.g. /etc/mkp/config.db).</param>
    public LiteDbApplianceConfigStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        DatabasePath = Path.GetFullPath(databasePath.Trim());
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _db = new LiteDatabase(DatabasePath);
        _collection = _db.GetCollection<ApplianceConfig>(CollectionName);
        _collection.EnsureIndex(x => x.Id, unique: true);
    }

    /// <inheritdoc />
    public string DatabasePath { get; }

    /// <summary>
    /// Resolves the default config DB path: <c>/etc/mkp/config.db</c> on Unix,
    /// or <c>%ProgramData%/MouseKeyProxy/mkp/config.db</c> on Windows.
    /// Override with <c>MKP_CONFIG_DB</c>.
    /// </summary>
    public static string ResolveDefaultDatabasePath()
    {
        var env = Environment.GetEnvironmentVariable("MKP_CONFIG_DB");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, "MouseKeyProxy", "mkp", "config.db");
        }

        return "/etc/mkp/config.db";
    }

    /// <inheritdoc />
    public ApplianceConfig Get()
    {
        lock (_gate)
        {
            return _collection.FindById(1) ?? new ApplianceConfig();
        }
    }

    /// <inheritdoc />
    public void Save(ApplianceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        lock (_gate)
        {
            var toSave = config with { Id = 1 };
            _collection.Upsert(toSave);
        }
    }

    /// <inheritdoc />
    public bool TryImportSeed(string seedJsonPath)
    {
        if (string.IsNullOrWhiteSpace(seedJsonPath) || !File.Exists(seedJsonPath))
        {
            return false;
        }

        lock (_gate)
        {
            if (_collection.FindById(1) is not null)
            {
                return false;
            }

            var json = File.ReadAllText(seedJsonPath);
            var seed = System.Text.Json.JsonSerializer.Deserialize<ApplianceConfig>(json, SeedJsonOptions)
                ?? new ApplianceConfig();
            _collection.Upsert(seed with { Id = 1 });
            return true;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _db.Dispose();
}
