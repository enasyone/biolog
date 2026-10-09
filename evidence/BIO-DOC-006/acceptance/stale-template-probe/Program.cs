using Biolog.Features.DocumentClassifier.Model;

var registry = new DocumentIntelligenceRegistry();
registry.Register(new DocumentIntelligenceRegistryEntry(
    "entry-v1", "UTO", "passport", "2013", DocumentSide.Front, 1, "template-v1", []));
registry.Register(new DocumentIntelligenceRegistryEntry(
    "entry-v2", "UTO", "passport", "2013", DocumentSide.Front, 2, "template-v2", []));

var current = registry.Match("UTO", "passport", "2013", DocumentSide.Front);
var retiredTemplate = registry.GetByTemplateId("template-v1");

Console.WriteLine($"current={current?.EntryId}; retired-template={retiredTemplate?.EntryId ?? "null"}");
if (current?.EntryId != "entry-v2" || retiredTemplate is not null)
{
    Console.Error.WriteLine("FAIL: a superseded registry template remains selectable by the classifier reverse lookup");
    return 1;
}

Console.WriteLine("PASS");
return 0;
