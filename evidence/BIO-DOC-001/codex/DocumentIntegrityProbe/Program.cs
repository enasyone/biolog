using Biolog.Features.DocumentVerify.Model;
using Biolog.Features.DocumentVerify.Storage;

var failures = 0;

try
{
    _ = BoundingBox.Create(double.NaN, 0, 0.2, 0.2);
    Console.Error.WriteLine("FAIL BoundingBox accepts NaN x coordinate");
    failures++;
}
catch (ArgumentOutOfRangeException)
{
    Console.WriteLine("PASS BoundingBox rejects NaN x coordinate");
}

var store = new InMemoryDocumentSessionStore();
const string tenant = "ten_synthetic";
const string session = "dcs_synthetic";
store.CreateSession(session, tenant, null, "2026-09-21T00:00:00Z");
for (var i = 0; i < 2; i++)
{
    store.AppendEvidence(tenant, session, new string((char)('a' + i), 64), "image/png", i + 1,
        DocumentSide.Front, EvidenceSource.Upload, "2026-09-21T00:00:00Z", "2026-09-21T00:00:01Z",
        EvidenceClassification.Sensitive);
}
store.AppendFieldEvidence(tenant, session, 1, "given_names", "SAME", "same", 0.9,
    FieldEvidenceSource.Ocr, null, "2026-09-21T00:00:02Z", EvidenceClassification.Personal);
store.AppendFieldEvidence(tenant, session, 2, "document_number", "SAME", "same", 0.9,
    FieldEvidenceSource.Ocr, null, "2026-09-21T00:00:03Z", EvidenceClassification.Personal);

try
{
    store.RaiseConflict(tenant, session, "date_of_birth", [1, 2], "2026-09-21T00:00:04Z");
    Console.Error.WriteLine("FAIL conflict accepts evidence from other fields with equal values");
    failures++;
}
catch (ArgumentException)
{
    Console.WriteLine("PASS conflict rejects unrelated/non-disagreeing evidence");
}

return failures == 0 ? 0 : 1;
