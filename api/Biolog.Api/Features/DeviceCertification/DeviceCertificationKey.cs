namespace Biolog.Api.Features.DeviceCertification;

/// <summary>
/// The certification matrix's natural key (BIO-DEV-002-AC01): a device+SDK+firmware
/// combination, matching <see cref="Features.DeviceRegistry.Device"/>'s own
/// <c>Vendor</c>/<c>Model</c>/<c>Firmware</c>/<c>Sdk</c> field names exactly, so a real
/// device's own fields can be looked up against this matrix directly (BIO-DEV-003's own
/// "unsupported firmware/SDK combinations surface certification status" AC will need
/// exactly that correspondence).
///
/// Deliberately not a <see cref="Identifiers.CanonicalId"/>: ADR-0001 §1.2 registers
/// exactly six identifier types and does not register one for a certification record;
/// minting a seventh prefix locally would need its own ADR amendment and Kemi's approval,
/// which this story's scope does not include. This is instead a "business identifier" in
/// ADR-0001 §1.6's sense: a natural composite key, not a canonical id.
/// </summary>
public sealed record DeviceCertificationKey(string Vendor, string Model, string Firmware, string Sdk)
{
    /// <summary>The composite string used for in-memory storage, per-aggregate locking
    /// and audit targets. See <see cref="DeviceCertificationKeyValidator"/> for why every
    /// field is validated to exclude '/' and ':' before this is ever computed - otherwise
    /// two distinct combinations could collide onto the same composite string.</summary>
    public string Composite => $"{Vendor}/{Model}/{Firmware}/{Sdk}";
}
