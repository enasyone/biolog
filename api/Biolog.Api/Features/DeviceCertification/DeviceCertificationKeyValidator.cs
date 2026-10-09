namespace Biolog.Api.Features.DeviceCertification;

/// <summary>
/// Validates each field of a <see cref="DeviceCertificationKey"/> (BIO-DEV-002-AC01).
/// Vendor/Model/Firmware/Sdk are free text describing real hardware and software
/// identifiers, but this feature also concatenates them with '/' to form the composite
/// key used for in-memory storage, per-aggregate locking and audit targets
/// (<see cref="DeviceCertificationKey.Composite"/>). If any field could itself contain
/// '/', two different combinations could collide onto the same composite string
/// (Vendor "A/B", Model "C" vs. Vendor "A", Model "B/C" both produce "A/B/C/..."),
/// silently merging two distinct certification records into one - the exact "a boundary
/// that looks closed but isn't" shape this codebase's reviewers have repeatedly found
/// elsewhere (BIO-DEV-001-QA-04). Rejecting '/' and ':' (also used to prefix a tenant
/// scope onto the composite key) closes that at the boundary rather than trusting every
/// future caller to avoid it.
/// </summary>
public static class DeviceCertificationKeyValidator
{
    private const int MaxFieldLength = 128;

    public static void Validate(DeviceCertificationKey key)
    {
        ValidateField(key.Vendor, nameof(key.Vendor));
        ValidateField(key.Model, nameof(key.Model));
        ValidateField(key.Firmware, nameof(key.Firmware));
        ValidateField(key.Sdk, nameof(key.Sdk));
    }

    private static void ValidateField(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDeviceCertificationKeyException($"{fieldName} must not be empty or whitespace-only");
        }

        if (value.Length > MaxFieldLength)
        {
            throw new InvalidDeviceCertificationKeyException($"{fieldName} must not exceed {MaxFieldLength} characters");
        }

        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                throw new InvalidDeviceCertificationKeyException($"{fieldName} must not contain control characters");
            }
        }

        if (value.Contains('/') || value.Contains(':'))
        {
            throw new InvalidDeviceCertificationKeyException(
                $"{fieldName} must not contain '/' or ':' - both are reserved as composite-key separators");
        }
    }
}
