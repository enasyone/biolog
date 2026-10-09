namespace Biolog.Api.Features.DeviceRegistry;

/// <summary>Input to register a new device (BIO-DEV-001-AC01). No <c>Id</c>, no
/// <c>TenantId</c>, no <c>Version</c> - all three are assigned by the service from the
/// resolved tenant context and the canonical id minter, never accepted from a caller.</summary>
public sealed record DeviceRegistrationRequest(
    string Serial,
    string Vendor,
    string Model,
    string Firmware,
    string Sdk,
    string Site,
    NetworkMetadata Network,
    string? CredentialReference);
