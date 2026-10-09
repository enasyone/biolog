from pathlib import Path

service = Path("api/features/bio-bio-007/Biolog.Features.FaceSearch/Service/FaceSearchService.cs").read_text()
project = Path("api/features/bio-bio-007/Biolog.Features.FaceSearch/Biolog.Features.FaceSearch.csproj").read_text()

index_method = service.split("public async Task IndexAsync(", 1)[1].split("public async Task<FaceSearchResult> SearchAsync", 1)[0]
accepts_raw_sample = "BiometricSample sample" in index_method
extracts_directly = "faceProvider.ExtractTemplateAsync(sample" in index_method
approval_check = any(token in index_method for token in ("Approved", "IsApproved", "FaceBiometricReference"))
project_references = [line for line in project.splitlines() if "<ProjectReference Include=" in line]
depends_on_enrolment = any("FaceEnrolment" in line or "bio-bio-003" in line.lower() for line in project_references)

print(f"index_accepts_raw_sample={accepts_raw_sample}")
print(f"index_extracts_template_directly={extracts_directly}")
print(f"index_checks_approval={approval_check}")
print(f"project_depends_on_bio_bio_003={depends_on_enrolment}")

if accepts_raw_sample and extracts_directly and not approval_check and not depends_on_enrolment:
    raise SystemExit(
        "FAIL: caller-supplied raw samples can be indexed into the search gallery without "
        "BIO-BIO-003 approval or equivalent approved-reference provenance"
    )

print("PASS")
