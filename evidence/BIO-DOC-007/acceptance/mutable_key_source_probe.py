from pathlib import Path

source = Path(
    "api/features/bio-doc-007/Biolog.Features.NfcReader/Model/NfcAccessKeys.cs"
).read_text()

public_arrays = [
    declaration
    for declaration in ("public byte[] EncryptionKey", "public byte[] MacKey")
    if declaration in source
]
defensive_getter = "ToArray()" in source or "Clone()" in source

print(f"public_mutable_array_getters={len(public_arrays)}")
print(f"defensive_copy_present={defensive_getter}")

if public_arrays and not defensive_getter:
    raise SystemExit(
        "FAIL: callers can mutate derived NFC key bytes through public array getters"
    )

print("PASS")
