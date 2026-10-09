namespace Biolog.Api.Features.DeviceCertification;

/// <summary>A certification entry already exists for this exact key and scope (the same
/// vendor+model+firmware+sdk combination, and the same tenant override or the same
/// platform-wide default). Registration is create-only by design - re-registering the
/// same combination must go through <see cref="DeviceCertificationService.UpdateGlobalAsync"/>/
/// <see cref="DeviceCertificationService.UpdateTenantOverrideAsync"/> so its version and
/// audit history are preserved, not silently replaced by a second "create".</summary>
public sealed class DeviceCertificationAlreadyExistsException(string vendor, string model, string firmware, string sdk)
    : Exception($"a certification entry already exists for {vendor}/{model}/{firmware}/{sdk} in this scope");
