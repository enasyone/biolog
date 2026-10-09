from pathlib import Path

source = Path("api/features/bio-doc-006/Biolog.Features.DocumentClassifier/Model/DocumentIntelligenceRegistry.cs").read_text()
register = source[source.index("public void Register"):source.index("public DocumentIntelligenceRegistryEntry? Match")]

has_collision_guard = "_byTemplateId.TryGetValue(entry.TemplateId" in register
if not has_collision_guard:
    print("FAIL: Register never checks whether TemplateId already belongs to a different current identity")
    raise SystemExit(1)

print("PASS: duplicate TemplateId ownership is guarded")
