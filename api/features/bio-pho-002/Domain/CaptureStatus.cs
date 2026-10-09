namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>
/// BIO-PHO-002-AC04: three terminal-or-pending states, no others. A capture that fails
/// <see cref="CaptureQualityGate"/> at submission starts life already <see cref="Rejected"/> -
/// it never passes through <see cref="Pending"/>. Only an explicit approval action (a mutation
/// entirely separate from submission) moves a <see cref="Pending"/> capture to
/// <see cref="Promoted"/>, which is also the only transition that ever calls
/// <see cref="IPhotoMasterPromotionGateway"/>. <see cref="Rejected"/> and <see cref="Promoted"/>
/// are both terminal - see <see cref="IBoothCaptureRepository"/>'s header for how illegal
/// transitions are structurally refused, not merely convention.
/// </summary>
public enum CaptureStatus
{
    Pending,
    Rejected,
    Promoted,
}
