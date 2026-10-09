using Biolog.Features.DocumentClassifier.Model;

var registry = new DocumentIntelligenceRegistry();
registry.Register(new DocumentIntelligenceRegistryEntry(
    "passport", "UTO", "passport", "2013", DocumentSide.Front, 1, "shared-template", []));
registry.Register(new DocumentIntelligenceRegistryEntry(
    "national-id", "UTO", "national-id", "2026", DocumentSide.Front, 1, "shared-template", []));

var passport = registry.Match("UTO", "passport", "2013", DocumentSide.Front);
var nationalId = registry.Match("UTO", "national-id", "2026", DocumentSide.Front);
var reverse = registry.GetByTemplateId("shared-template");

Console.WriteLine($"passport={passport?.EntryId}; national-id={nationalId?.EntryId}; reverse={reverse?.EntryId}");
if (passport is not null && nationalId is not null && !ReferenceEquals(passport, reverse))
{
    Console.Error.WriteLine("FAIL: one TemplateId names two current identities while classification silently resolves only the last one");
    return 1;
}

return 0;
