from pathlib import Path

source = Path("api/features/bio-doc-006/Biolog.Features.DocumentClassifier/Model/DocumentIntelligenceRegistry.cs").read_text()

assert "_byIdentity[key] = entry;" in source
assert "_byTemplateId[entry.TemplateId] = entry;" in source

if "_byTemplateId.Remove(existing.TemplateId)" not in source:
    print("FAIL: Register replaces the identity entry but never removes the superseded template from the classifier reverse lookup")
    raise SystemExit(1)

print("PASS: superseded template reverse mapping is removed")
