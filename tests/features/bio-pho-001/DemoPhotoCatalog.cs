using System.Security.Cryptography;
using System.Text;
using Biolog.Features.Photography.Audit;
using Biolog.Features.Photography.Domain;

namespace Biolog.Features.Photography.Tests;

/// <summary>
/// BIO-PHO-001-AC05: "synthetic demo photos remain consistently assigned in tests." A small,
/// deterministic demo dataset - the same person id always produces byte-identical synthetic
/// content (<see cref="SyntheticContentFor"/>, derived from a stable hash of the id, never
/// <see cref="Random"/> or a clock), and <see cref="SeedIdempotentlyAsync"/> is safe to call
/// any number of times for the same person: the first call creates the master and assignment,
/// every later call is a no-op that returns the SAME assignment rather than appending a
/// duplicate master version or reassigning to a new one. A demo/seed dataset that silently drew
/// a new random photo, or created a new master version, on every test run or re-seed would make
/// screenshots, fixtures and repeated test runs flaky in exactly the way this AC exists to rule
/// out.
/// </summary>
internal static class DemoPhotoCatalog
{
    /// <summary>A fixed-size, fully deterministic PNG: width/height are derived from a SHA-256 hash of <paramref name="personId"/>, never from <see cref="Random"/> or wall-clock time, so the same id always yields byte-identical content across processes and runs.</summary>
    public static byte[] SyntheticContentFor(string personId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(personId));
        var width = 100 + (hash[0] % 50);
        var height = 100 + (hash[1] % 50);
        return SyntheticImages.Png(width, height);
    }

    /// <summary>Appends a master and assigns it for <paramref name="personId"/> if, and only if, no assignment already exists - returns the existing assignment unchanged on every later call, so re-seeding a demo dataset is always safe.</summary>
    public static async Task<PhotoAssignment> SeedIdempotentlyAsync(
        PhotoMasterService masters, PhotoAssignmentService assignments, string personId, CancellationToken cancellationToken)
    {
        var existing = await assignments.GetAsync(personId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var actor = new AuditActor(ActorType.System, "demo_seed");
        var master = await masters.AppendAsync(personId, SyntheticContentFor(personId), actor, CanonicalCorrelationId(personId, "master"), cancellationToken);
        return await assignments.AssignAsync(personId, master.Version, expectedVersion: null, actor, CanonicalCorrelationId(personId, "assign"), cancellationToken);
    }

    /// <summary>Builds a well-formed canonical event id (`evt_&lt;UUIDv7 shape&gt;`) deterministically from a hash, forcing the version/variant nibbles the shape requires without ever drawing from <see cref="Random"/>.</summary>
    private static string CanonicalCorrelationId(string personId, string salt)
    {
        var hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(personId + salt)));
        return $"evt_{hex[..8]}-{hex[8..12]}-7{hex[12..15]}-8{hex[15..18]}-{hex[18..30]}";
    }
}
