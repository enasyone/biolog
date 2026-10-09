namespace Biolog.Api.Features.BrandAssets;

/// <summary>
/// Upload types a tenant designer may assign (BIO-CRD-001-AC01/AC02), per the approved
/// Card Designer v0.3 specification section 9.1.
/// </summary>
public enum BrandAssetType
{
    PrimaryCrest,
    StaffLogo,
    StudentLogo,
    SubsidiaryLogo,
    BranchCampusLogo,
    FacultyCollegeLogo,
    SpecialProgrammeMark,
    WatermarkSecurityArtwork,
}

/// <summary>Lifecycle status of one asset version (BIO-CRD-001-AC02).</summary>
public enum BrandAssetStatus
{
    Pending,
    Approved,
    Rejected,
    Archived,
}

/// <summary>Upload container format (BIO-CRD-001-AC01). SVG is scalable; PNG/JPEG are raster.</summary>
public enum BrandAssetFormat
{
    Png,
    Jpeg,
    Svg,
}

/// <summary>
/// Raster quality assessment computed at upload time (BIO-CRD-001-AC03), per Card Designer
/// v0.3 section 9.2. Always <c>null</c> for SVG - vector artwork is scalable by construction
/// and never carries a false raster-DPI warning.
/// </summary>
public sealed record RasterQualityAssessment(
    int PixelWidth,
    int PixelHeight,
    bool HasAlphaChannel,
    /// <summary>Native DPI declared by the file's own metadata (PNG pHYs / JFIF APP0), if any.</summary>
    double? NativeDpi,
    /// <summary>
    /// DPI at the caller-supplied intended print size, if one was given. Null when no
    /// intended physical size was supplied and the file carries no native DPI either -
    /// there is then nothing to warn about, not a warning by default.
    /// </summary>
    double? EffectiveDpi,
    bool LowResolutionWarning,
    string? WarningMessage
);

/// <summary>Outcome of content/structure validation (BIO-CRD-001-AC04).</summary>
public sealed record ContentValidationResult(bool IsValid, IReadOnlyList<string> Messages);

/// <summary>Outcome of the malware-scanning hook (BIO-CRD-001-AC04).</summary>
public enum MalwareScanVerdict
{
    Clean,
    Flagged,
}

public sealed record MalwareScanResult(MalwareScanVerdict Verdict, string ScannerName, IReadOnlyList<string> Findings);

/// <summary>
/// One version of a tenant brand asset (BIO-CRD-001-AC02). <see cref="LineageId"/> groups
/// every version of "the same" logical asset (e.g. re-uploads of the primary crest); each
/// version is otherwise an independent, immutable record once stored - a re-upload creates a
/// new version rather than mutating an existing one, matching the "versioned" library the
/// story title names and the published-template-immutability precedent used elsewhere in the
/// Card Designer spec.
/// </summary>
public sealed record BrandAsset
{
    public required string Id { get; init; }
    public required string LineageId { get; init; }
    public required int Version { get; init; }
    public required string TenantId { get; init; }
    public string? OrganisationId { get; init; }
    public string? CampusId { get; init; }
    public required BrandAssetType AssetType { get; init; }
    public required BrandAssetStatus Status { get; init; }
    public required BrandAssetFormat Format { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required int SizeBytes { get; init; }
    public required byte[] Content { get; init; }
    public RasterQualityAssessment? RasterQuality { get; init; }
    public required ContentValidationResult ContentValidation { get; init; }
    public required MalwareScanResult MalwareScan { get; init; }
    public required string UploadedBy { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}
