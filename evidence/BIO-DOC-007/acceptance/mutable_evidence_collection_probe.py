from pathlib import Path

session = Path(
    "api/features/bio-doc-007/Biolog.Features.NfcReader/Model/ChipReadSession.cs"
).read_text()
outcome = Path(
    "api/features/bio-doc-007/Biolog.Features.NfcReader/Model/ChipReadOutcome.cs"
).read_text()

mutable_list_created = "var read = new List<ChipDataGroupRead>()" in session
same_list_passed = "dataGroupsRead: read" in session
same_list_exposed = "DataGroupsRead = dataGroupsRead" in outcome
immutable_copy_present = (
    "ToArray()" in outcome
    or "AsReadOnly()" in outcome
    or "ImmutableArray" in outcome
)

print(f"mutable_list_created={mutable_list_created}")
print(f"same_list_passed_to_result={same_list_passed}")
print(f"same_list_exposed_as_readonly_interface={same_list_exposed}")
print(f"immutable_copy_present={immutable_copy_present}")

if mutable_list_created and same_list_passed and same_list_exposed and not immutable_copy_present:
    raise SystemExit(
        "FAIL: DataGroupsRead exposes a List through IReadOnlyList; callers can cast and mutate "
        "the chip evidence after passive authentication"
    )

print("PASS")
