using Biolog.Api.Features.DeviceRegistry;
using Biolog.Api.Tenancy;

const string plaintextCredential = "synthetic-plaintext-password";
var repository = new InMemoryDeviceRepository();
var tenant = new TenantContext("ten_00000000-0000-7000-8000-000000000001", "synthetic", Array.Empty<string>());
var request = new DeviceRegistrationRequest(
    "SERIAL-1",
    "Synthetic Vendor",
    "Model 1",
    "1.0",
    "1.0",
    "Synthetic Site",
    new NetworkMetadata(null, null, null),
    plaintextCredential);

var stored = await repository.CreateAsync(tenant, request, CancellationToken.None);
var returned = await repository.GetAsync(tenant, stored.Id, CancellationToken.None);

if (returned?.CredentialReference == plaintextCredential)
{
    Console.WriteLine("FAIL plaintext credential accepted, stored and returned unchanged");
    return 1;
}

Console.WriteLine("PASS plaintext credential rejected");
return 0;
