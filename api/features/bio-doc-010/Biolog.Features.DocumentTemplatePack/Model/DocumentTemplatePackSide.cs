using Biolog.Features.DocumentClassifier.Model;

namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>
/// One physical side of a governed document identity (AC01's "sides"), naming the BIO-DOC-004 OCR
/// template (by id, loosely coupled - see <see cref="MrzMetadata"/>'s own remarks) used to read it.
/// <see cref="DocumentTemplatePack.Publish"/> turns each of an entry's sides into its own
/// <see cref="DocumentIntelligenceRegistryEntry"/> registration.
/// </summary>
public sealed record DocumentTemplatePackSide(DocumentSide Side, string TemplateId);
