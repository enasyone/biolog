using System.Security.Cryptography;

namespace Biolog.Features.Authentication;

/// <summary>
/// Synthetic in-memory password directory backing <see cref="PasswordProviderAdapter"/> only -
/// a real BIOLOG password provider would sit in front of a real credential store; this is a
/// deterministic stand-in for demo/test registration, the same "own the synthetic seed data"
/// shape BIO-DEV-003's vendor SDK simulators use. Passwords are PBKDF2-hashed (never stored or
/// compared in plaintext) with a per-record random salt; verification is constant-time.
/// </summary>
/// <summary>HTTP registration input for a password credential row - only meaningful against a
/// <see cref="AuthProviderType.Password"/> provider config.</summary>
public sealed record PasswordCredentialRegistrationRequest(string Username, string Password, IReadOnlyList<ProviderClaim> Claims);

public interface ICredentialStore
{
    Task RegisterAsync(string tenantId, string providerId, string username, string password, IReadOnlyList<ProviderClaim> claims, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderClaim>?> VerifyAsync(string tenantId, string providerId, string username, string password, CancellationToken cancellationToken);
}

public sealed class InMemoryCredentialStore : ICredentialStore
{
    private const int Iterations = 100_000;
    private const int HashLength = 32;

    private sealed record Record(byte[] Salt, byte[] Hash, IReadOnlyList<ProviderClaim> Claims);

    private readonly Dictionary<(string, string, string), Record> _records = [];
    private readonly Lock _gate = new();

    public Task RegisterAsync(string tenantId, string providerId, string username, string password, IReadOnlyList<ProviderClaim> claims, CancellationToken cancellationToken)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashLength);

        lock (_gate)
        {
            _records[(tenantId, providerId, username)] = new Record(salt, hash, claims);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProviderClaim>?> VerifyAsync(string tenantId, string providerId, string username, string password, CancellationToken cancellationToken)
    {
        Record? record;
        lock (_gate)
        {
            _records.TryGetValue((tenantId, providerId, username), out var found);
            record = found;
        }

        if (record is null)
        {
            return Task.FromResult<IReadOnlyList<ProviderClaim>?>(null);
        }

        var candidateHash = Rfc2898DeriveBytes.Pbkdf2(password, record.Salt, Iterations, HashAlgorithmName.SHA256, HashLength);
        var matches = CryptographicOperations.FixedTimeEquals(candidateHash, record.Hash);
        return Task.FromResult(matches ? record.Claims : null);
    }
}
