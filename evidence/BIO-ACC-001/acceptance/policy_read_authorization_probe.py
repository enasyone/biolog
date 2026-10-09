from pathlib import Path

endpoints = Path("api/features/bio-acc-001/Api/AccessPolicyEndpoints.cs").read_text()
service = Path("api/features/bio-acc-001/AccessPolicyService.cs").read_text()

get_endpoint = endpoints.split("private static Task GetAsync(", 1)[1].split("private static Task ListAsync(", 1)[0]
list_endpoint = endpoints.split("private static Task ListAsync(", 1)[1].split("internal static async Task<T> ReadJsonAsync", 1)[0]
get_service = service.split("public async Task<Domain.AccessPolicy> GetAsync(", 1)[1].split("public Task<IReadOnlyList<Domain.AccessPolicy>> ListAsync", 1)[0]
list_service = service.split("ListAsync(AuditActor actor", 1)[1].split("private async Task AuditAsync", 1)[0]

def protected(endpoint: str, service_method: str) -> bool:
    endpoint_gate = "ActorResolver.Resolve" in endpoint and "provenanceGate.Require" in endpoint
    permission_gate = "RequirePermissionAsync" in service_method or "ActorHasPermissionAsync" in service_method
    return endpoint_gate and permission_gate

print(f"get_read_protected={protected(get_endpoint, get_service)}")
print(f"list_read_protected={protected(list_endpoint, list_service)}")
print(f"get_exposes_full_policy={'AccessPolicyResponse.From(policy)' in get_endpoint}")
print(f"list_exposes_full_policies={'AccessPolicyResponse.From' in list_endpoint}")

if not protected(get_endpoint, get_service) or not protected(list_endpoint, list_service):
    raise SystemExit(
        "FAIL: GET/list expose tenant access-policy rules without actor provenance or permission checks"
    )

print("PASS")
