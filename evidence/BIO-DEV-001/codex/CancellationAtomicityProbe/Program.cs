using Biolog.Api.Audit;
using Biolog.Api.Features.DeviceRegistry;
using Biolog.Api.Tenancy;
using Microsoft.Extensions.Logging;

var tenant = new TenantContext("ten_probe", "binding-probe", []);
var repository = new InMemoryDeviceRepository();
using var cancellation = new CancellationTokenSource();
var logger = new CancellingLogger(cancellation);
var auditSink = new InMemoryAuditSink(logger);
var service = new DeviceService(
    new TenantContextAccessor { Context = tenant }, repository, auditSink, new DeviceLockRegistry());

var device = await service.RegisterAsync(new DeviceRegistrationRequest(
    "SN-PROBE", "Synthetic", "Probe", "1.0", "sdk-1", "Test Site",
    new NetworkMetadata("192.0.2.10", "02:00:00:00:00:01", "probe-device"),
    "secretref://synthetic/device"), CancellationToken.None);

var updated = await service.ChangeStatusAsync(
    device.Id, DeviceStatus.Active, device.Version,
    new AuditActor("user", "usr_probe"), null, cancellation.Token);
var stored = await repository.GetAsync(tenant, device.Id, CancellationToken.None);
var audit = auditSink.Events.Single();

if (updated?.Status == DeviceStatus.Active &&
    updated.Version == 2 &&
    stored == updated &&
    audit.Outcome == AuditOutcome.Allowed &&
    audit.ReasonCode == "status_updated")
{
    Console.WriteLine("PASS cancellation after audit no longer abandons the matching repository commit");
    return 0;
}

Console.Error.WriteLine("FAIL audit and committed device state do not match");
return 1;

file sealed class CancellingLogger(CancellationTokenSource cancellation) : ILogger<InMemoryAuditSink>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => cancellation.Cancel();
}
