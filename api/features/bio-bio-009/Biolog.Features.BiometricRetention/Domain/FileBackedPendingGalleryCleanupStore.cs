using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>
/// Round 4 (god's dispatch): the round-3 <see cref="InMemoryPendingGalleryCleanupStore"/>
/// erased its only recovery record on every process restart - defeating the entire point of
/// calling the obligation "durable." This implementation persists every write to a JSON file on
/// disk (write-to-temp-then-replace, so a crash mid-write can never leave a half-written, corrupt
/// file) and reads it fresh on every call - two separate instances pointed at the same file path
/// see each other's writes, the same shape a real process restart has. A full external database
/// does not exist anywhere in this codebase (every store in this system is an in-memory
/// singleton); this is the minimal mechanism that genuinely satisfies "survives restart" (the
/// documented "at minimum" bar for this fix) without inventing production datastore
/// infrastructure this story was never asked to build.
///
/// <see cref="InMemoryPendingGalleryCleanupStore"/> is kept for this feature's own pure unit
/// tests, which must not touch the filesystem; <see cref="PendingGalleryCleanupStoreProvisioning"/>
/// is what the real, registered production implementation uses.
/// </summary>
internal sealed class FileBackedPendingGalleryCleanupStore : IPendingGalleryCleanupStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public FileBackedPendingGalleryCleanupStore(string filePath)
    {
        _filePath = filePath;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task RecordAsync(PendingGalleryCleanup entry, CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            var all = await ReadAllAsync(cancellationToken);
            all[Key(entry.TenantId, entry.Kind, entry.PersonId)] = entry;
            await WriteAllAsync(all, cancellationToken);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task RemoveAsync(string tenantId, BiometricRecordKind kind, string personId, CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            var all = await ReadAllAsync(cancellationToken);
            if (all.Remove(Key(tenantId, kind, personId)))
            {
                await WriteAllAsync(all, cancellationToken);
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task<IReadOnlyList<PendingGalleryCleanup>> ListAsync(string tenantId, CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            var all = await ReadAllAsync(cancellationToken);
            IReadOnlyList<PendingGalleryCleanup> result = all.Values.Where(entry => entry.TenantId == tenantId).ToList();
            return result;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private static string Key(string tenantId, BiometricRecordKind kind, string personId) => $"{tenantId}|{kind}|{personId}";

    private async Task<Dictionary<string, PendingGalleryCleanup>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, PendingGalleryCleanup>();
        }

        await using var stream = File.OpenRead(_filePath);
        var loaded = await JsonSerializer.DeserializeAsync<Dictionary<string, PendingGalleryCleanup>>(stream, cancellationToken: cancellationToken);
        return loaded ?? new Dictionary<string, PendingGalleryCleanup>();
    }

    private async Task WriteAllAsync(Dictionary<string, PendingGalleryCleanup> all, CancellationToken cancellationToken)
    {
        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, all, cancellationToken: cancellationToken);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}

/// <summary>Reads <c>BiometricRetention:PendingGalleryCleanupStorePath</c> - this feature's own
/// configuration key, the same out-of-band provisioning shape <see cref="Api.ActorProvenanceProvisioning"/>
/// already established. Defaults to a file next to the running assembly when unconfigured, so
/// the durability fix applies out of the box, not only when explicitly configured.</summary>
internal static class PendingGalleryCleanupStoreProvisioning
{
    public const string ConfigurationKey = "BiometricRetention:PendingGalleryCleanupStorePath";

    public static IPendingGalleryCleanupStore LoadFromConfiguration(IConfiguration configuration)
    {
        var configuredPath = configuration[ConfigurationKey];
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "biometric-retention-pending-gallery-cleanup.json")
            : configuredPath;
        return new FileBackedPendingGalleryCleanupStore(path);
    }
}
