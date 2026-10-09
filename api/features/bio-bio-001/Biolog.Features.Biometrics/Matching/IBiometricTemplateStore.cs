using Biolog.Features.Biometrics.Domain;

namespace Biolog.Features.Biometrics.Matching;

/// <summary>
/// BIO-BIO-001-AC03: "Biometric service is separate from MySQL matching logic." This is the
/// persistence seam <see cref="Service.BiometricService"/> talks to instead of ever holding a
/// connection string, an ORM context or a SQL statement itself. This story ships only
/// <see cref="Synthetic.InMemoryBiometricTemplateStore"/> (AC05: no real biometric data required
/// locally); a later story implements this interface against a real (e.g. MySQL) datastore
/// without <see cref="Service.BiometricService"/> or either provider interface changing shape -
/// the same one-way, interface-first seam BIO-DOC-001 established for
/// <c>IDocumentSessionStore</c>.
/// </summary>
public interface IBiometricTemplateStore
{
    Task SaveAsync(BiometricTemplate template, CancellationToken cancellationToken);

    /// <summary>All templates enrolled for one person/modality, newest first. Empty (never null)
    /// when nothing is enrolled.</summary>
    Task<IReadOnlyList<BiometricTemplate>> GetTemplatesAsync(
        string tenantId, string personId, BiometricModality modality, CancellationToken cancellationToken);
}
