using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Biolog.Api.Features.BrandAssets;

/// <summary>
/// Route handlers for the versioned tenant brand asset library (BIO-CRD-001). Kept as plain
/// static methods mapped from <see cref="BrandAssetsModule"/> rather than MVC controllers,
/// matching this host's existing minimal-API style (<c>HealthModule</c>).
/// </summary>
public static class BrandAssetEndpoints
{
    public const int MaxUploadBytes = 10 * 1024 * 1024; // 10 MB: generous for a logo/crest, bounded against abuse.
    public const int DefaultPageLimit = 20;
    public const int MaxPageLimit = 100;

    public static async Task<IResult> UploadNewAsync(
        HttpRequest request,
        ITenantAuthenticator tenantAuthenticator,
        IMalwareScanner scanner,
        IBrandAssetRepository repository,
        IIdempotencyStore idempotencyStore,
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant(request, tenantAuthenticator);
        var idempotencyKey = RequireIdempotencyKey(request);
        var processed = await ParseAndValidateUploadAsync(request, scanner, cancellationToken);
        var fingerprint = ComputeFingerprint(processed);

        return await WithIdempotencyAsync(idempotencyStore, tenantId, "brand-assets.upload", idempotencyKey, fingerprint, () =>
        {
            var now = DateTimeOffset.UtcNow;
            var asset = new BrandAsset
            {
                Id = NewId(),
                LineageId = NewId(),
                Version = 1,
                TenantId = tenantId,
                OrganisationId = processed.OrganisationId,
                CampusId = processed.CampusId,
                AssetType = processed.AssetType,
                Status = BrandAssetStatus.Pending,
                Format = processed.Format,
                FileName = processed.FileName,
                ContentType = processed.ContentType,
                SizeBytes = processed.FileBytes.Length,
                Content = processed.FileBytes,
                RasterQuality = processed.RasterQuality,
                ContentValidation = processed.ContentValidation,
                MalwareScan = processed.MalwareScan,
                UploadedBy = "local-dev-user", // BIO-CRD-001 has no auth/identity story wired in yet.
                CreatedAtUtc = now,
            };
            repository.Add(asset);
            return (201, BrandAssetResponse.From(asset));
        });
    }

    public static async Task<IResult> UploadNewVersionAsync(
        HttpRequest request,
        string lineageId,
        ITenantAuthenticator tenantAuthenticator,
        IMalwareScanner scanner,
        IBrandAssetRepository repository,
        IIdempotencyStore idempotencyStore,
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant(request, tenantAuthenticator);
        var idempotencyKey = RequireIdempotencyKey(request);
        var previous = repository.GetLatestVersion(tenantId, lineageId) ?? throw new BrandAssetNotFoundException();
        var processed = await ParseAndValidateUploadAsync(request, scanner, cancellationToken);
        var fingerprint = ComputeFingerprint(processed);

        return await WithIdempotencyAsync(idempotencyStore, tenantId, "brand-assets.upload-version", idempotencyKey, fingerprint, () =>
        {
            var asset = new BrandAsset
            {
                Id = NewId(),
                LineageId = lineageId,
                Version = previous.Version + 1,
                TenantId = tenantId,
                OrganisationId = processed.OrganisationId ?? previous.OrganisationId,
                CampusId = processed.CampusId ?? previous.CampusId,
                AssetType = previous.AssetType,
                Status = BrandAssetStatus.Pending,
                Format = processed.Format,
                FileName = processed.FileName,
                ContentType = processed.ContentType,
                SizeBytes = processed.FileBytes.Length,
                Content = processed.FileBytes,
                RasterQuality = processed.RasterQuality,
                ContentValidation = processed.ContentValidation,
                MalwareScan = processed.MalwareScan,
                UploadedBy = "local-dev-user",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            repository.Add(asset);
            return (201, BrandAssetResponse.From(asset));
        });
    }

    public static IResult List(
        HttpRequest request,
        ITenantAuthenticator tenantAuthenticator,
        IBrandAssetRepository repository,
        string? assetType,
        string? status,
        string? organisationId,
        string? campusId,
        int? limit,
        string? cursor)
    {
        var tenantId = RequireTenant(request, tenantAuthenticator);
        var errors = new List<ValidationErrorItem>();

        BrandAssetType? parsedAssetType = null;
        if (assetType is not null)
        {
            if (TryParseEnum<BrandAssetType>(assetType, out var parsedAssetTypeValue))
            {
                parsedAssetType = parsedAssetTypeValue;
            }
            else
            {
                errors.Add(new ValidationErrorItem("asset_type", "invalid_format", $"unknown asset_type: {assetType}"));
            }
        }
        BrandAssetStatus? parsedStatus = null;
        if (status is not null)
        {
            if (TryParseEnum<BrandAssetStatus>(status, out var parsedStatusValue))
            {
                parsedStatus = parsedStatusValue;
            }
            else
            {
                errors.Add(new ValidationErrorItem("status", "invalid_format", $"unknown status: {status}"));
            }
        }
        var effectiveLimit = limit ?? DefaultPageLimit;
        if (effectiveLimit is < 1 or > MaxPageLimit)
        {
            errors.Add(new ValidationErrorItem("limit", "out_of_range", $"limit must be between 1 and {MaxPageLimit}"));
        }
        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        var page = repository.List(
            tenantId, new BrandAssetListFilter(parsedAssetType, parsedStatus, organisationId, campusId), effectiveLimit, cursor);
        var response = new BrandAssetPageResponse(
            page.Items.Select(BrandAssetResponse.From).ToList(), new PageInfo(page.NextCursor, effectiveLimit));
        return Results.Json(response, BrandAssetJson.Options);
    }

    public static IResult GetById(HttpRequest request, string id, ITenantAuthenticator tenantAuthenticator, IBrandAssetRepository repository)
    {
        var tenantId = RequireTenant(request, tenantAuthenticator);
        var asset = repository.GetById(tenantId, id) ?? throw new BrandAssetNotFoundException();
        return Results.Json(BrandAssetResponse.From(asset), BrandAssetJson.Options);
    }

    public static async Task<IResult> UpdateStatusAsync(
        HttpRequest request, string id, ITenantAuthenticator tenantAuthenticator, IBrandAssetRepository repository, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant(request, tenantAuthenticator);
        UpdateStatusRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<UpdateStatusRequest>(
                request.Body, BrandAssetJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            throw new ValidationFailedException([new ValidationErrorItem("body", "invalid_format", "request body is not valid JSON")]);
        }
        if (body is null || !TryParseEnum<BrandAssetStatus>(body.Status, out var newStatus))
        {
            throw new ValidationFailedException([new ValidationErrorItem("status", "invalid_format", "status must be one of Pending, Approved, Rejected, Archived")]);
        }

        var updated = repository.UpdateStatus(tenantId, id, newStatus) ?? throw new BrandAssetNotFoundException();
        return Results.Json(BrandAssetResponse.From(updated), BrandAssetJson.Options);
    }

    // --- shared helpers ---

    private static string RequireTenant(HttpRequest request, ITenantAuthenticator authenticator) =>
        authenticator.TryAuthenticate(request) ?? throw new TenantAuthenticationFailedException();

    private static string RequireIdempotencyKey(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) || string.IsNullOrWhiteSpace(values.ToString()))
        {
            throw new IdempotencyKeyRequiredException();
        }
        var key = values.ToString();
        if (key.Length > 255)
        {
            throw new ValidationFailedException([new ValidationErrorItem("idempotency-key", "out_of_range", "Idempotency-Key must be at most 255 characters")]);
        }
        return key;
    }

    private sealed record ProcessedUpload(
        BrandAssetType AssetType,
        string? OrganisationId,
        string? CampusId,
        double? IntendedWidthMm,
        byte[] FileBytes,
        string FileName,
        string ContentType,
        BrandAssetFormat Format,
        RasterQualityAssessment? RasterQuality,
        ContentValidationResult ContentValidation,
        MalwareScanResult MalwareScan);

    private static async Task<ProcessedUpload> ParseAndValidateUploadAsync(
        HttpRequest request, IMalwareScanner scanner, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            throw new ValidationFailedException([new ValidationErrorItem("body", "invalid_format", "request must be multipart/form-data")]);
        }
        var form = await request.ReadFormAsync(cancellationToken);
        var errors = new List<ValidationErrorItem>();

        var file = form.Files["file"];
        byte[] fileBytes = [];
        if (file is null || file.Length == 0)
        {
            errors.Add(new ValidationErrorItem("file", "required", "a file is required"));
        }
        else if (file.Length > MaxUploadBytes)
        {
            errors.Add(new ValidationErrorItem("file", "out_of_range", $"file exceeds the {MaxUploadBytes} byte limit"));
        }
        else
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            fileBytes = stream.ToArray();
        }

        BrandAssetType assetType = default;
        if (!form.TryGetValue("assetType", out var assetTypeRaw) || !TryParseEnum(assetTypeRaw.ToString(), out assetType))
        {
            errors.Add(new ValidationErrorItem("asset_type", "required", "asset_type is required and must be one of the supported brand asset types"));
        }

        double? intendedWidthMm = null;
        if (form.TryGetValue("intendedWidthMm", out var intendedWidthRaw) && !string.IsNullOrWhiteSpace(intendedWidthRaw))
        {
            if (!double.TryParse(intendedWidthRaw, out var parsedWidth) || parsedWidth <= 0)
            {
                errors.Add(new ValidationErrorItem("intended_width_mm", "out_of_range", "intended_width_mm must be a positive number"));
            }
            else
            {
                intendedWidthMm = parsedWidth;
            }
        }

        BrandAssetFormat? format = fileBytes.Length > 0 ? BrandAssetFormatDetector.Detect(fileBytes) : null;
        if (fileBytes.Length > 0 && format is null)
        {
            errors.Add(new ValidationErrorItem("file", "invalid_format", "unsupported file format; PNG, JPEG and SVG are supported"));
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        // Malware scanning hook runs before content-specific validation and before anything
        // is stored (BIO-CRD-001-AC04): a flagged upload is refused outright, never persisted.
        var scanResult = scanner.Scan(fileBytes, file!.ContentType);
        if (scanResult.Verdict == MalwareScanVerdict.Flagged)
        {
            throw new MalwareDetectedException(scanResult.Findings);
        }

        RasterQualityAssessment? rasterQuality = null;
        ContentValidationResult contentValidation;
        if (format == BrandAssetFormat.Svg)
        {
            contentValidation = SvgValidator.Validate(fileBytes);
            if (!contentValidation.IsValid)
            {
                throw new ValidationFailedException(
                    contentValidation.Messages.Select(m => new ValidationErrorItem("file", "active_content", m)).ToList());
            }
        }
        else
        {
            var header = format == BrandAssetFormat.Png
                ? RasterImageInspector.TryReadPng(fileBytes)
                : RasterImageInspector.TryReadJpeg(fileBytes);
            if (header is null)
            {
                throw new ValidationFailedException([new ValidationErrorItem("file", "invalid_format", "file header could not be parsed for its detected format")]);
            }
            rasterQuality = RasterImageInspector.Assess(header.Value, intendedWidthMm);
            contentValidation = new ContentValidationResult(true, []);
        }

        return new ProcessedUpload(
            assetType, Blank(form, "organisationId"), Blank(form, "campusId"), intendedWidthMm,
            fileBytes, file.FileName, file.ContentType, format!.Value, rasterQuality, contentValidation, scanResult);
    }

    private static string? Blank(IFormCollection form, string key) =>
        form.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.ToString() : null;

    private static bool TryParseEnum<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result);

    private static string ComputeFingerprint(ProcessedUpload upload)
    {
        using var sha256 = SHA256.Create();
        using var buffer = new MemoryStream();
        void WriteString(string? s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "\0");
            buffer.Write(bytes);
            buffer.WriteByte(0x1F); // unit separator: keeps fields from being concatenation-ambiguous
        }
        WriteString(upload.AssetType.ToString());
        WriteString(upload.OrganisationId);
        WriteString(upload.CampusId);
        WriteString(upload.IntendedWidthMm?.ToString("R"));
        buffer.Write(upload.FileBytes);
        return Convert.ToHexStringLower(sha256.ComputeHash(buffer.ToArray()));
    }

    private static async Task<IResult> WithIdempotencyAsync(
        IIdempotencyStore store, string tenantId, string operation, string idempotencyKey, string fingerprint,
        Func<(int Status, object Body)> execute)
    {
        var (won, existing) = store.Claim(tenantId, operation, idempotencyKey, fingerprint);
        if (!won)
        {
            var record = existing!;
            if (record.Fingerprint != fingerprint)
            {
                throw new IdempotencyKeyReuseException();
            }
            if (record.State == IdempotencyRecordState.Done)
            {
                return Results.Json(
                    JsonSerializer.Deserialize<JsonElement>(record.ResponseBodyJson!), BrandAssetJson.Options,
                    statusCode: record.ResponseStatus);
            }
            // A genuine concurrent retry of the identical in-flight request: wait briefly
            // rather than executing a second time or hanging forever (ADR-0004 §4.4).
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
                var latest = store.Get(tenantId, operation, idempotencyKey);
                if (latest?.State == IdempotencyRecordState.Done)
                {
                    return Results.Json(
                        JsonSerializer.Deserialize<JsonElement>(latest.ResponseBodyJson!), BrandAssetJson.Options,
                        statusCode: latest.ResponseStatus);
                }
            }
            throw new IdempotencyWaitTimeoutException();
        }

        try
        {
            var (status, body) = execute();
            var bodyJson = JsonSerializer.Serialize(body, BrandAssetJson.Options);
            store.Complete(tenantId, operation, idempotencyKey, status, bodyJson);
            return Results.Json(body, BrandAssetJson.Options, statusCode: status);
        }
        catch
        {
            // An infrastructure-level failure, not a business result worth replaying: free
            // the claim so a retry can try again (ADR-0004 §4.4).
            store.Release(tenantId, operation, idempotencyKey);
            throw;
        }
    }

    private static string NewId() => Guid.CreateVersion7().ToString();
}
