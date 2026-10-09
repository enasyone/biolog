using Biolog.Api.Features.BrandAssets;
using Biolog.Api.Modules;

var builder = WebApplication.CreateBuilder(args);

// The module list is the one place that declares what this host does. See
// Modules/IBiologModule.cs and Modules/ModuleHost.cs (BIO-FND-008-AC05).
var moduleHost = new ModuleHost(new IBiologModule[]
{
    new HealthModule(),
    new OpenApiModule(),
    new TenantModule(),
    new ObservabilityModule(),
    new DeviceRegistryModule(),
    new DeviceCertificationModule(),
    new DocumentModule(),
    new BioTen001Module(),
    new BioTen002Module(),
    new BrandAssetsModule(),
    new BioIdn001Module(),
    new BioIdn002Module(),
    new BioIdn003Module(),
    new BioIdn004Module(),
    new BioIdn005Module(),
    new BioIdn006Module(),
    new BioBio001Module(),
    new BioPho001Module(),
    new BioPho002Module(),
    new BioBio002Module(),
    new BioCrd002Module(),
    new BioBio003Module(),
    new BioPho003Module(),
    new BioCrd003Module(),
    new BioCrd004Module(),
    new BioCrd008Module(),
    new BioBio004Module(),
    new BioCrd005Module(),
    new BioBio005Module(),
    new BioBio006Module(),
    new BioCrd006Module(),
    new BioBio007Module(),
    new BioAcc001Module(),
    new BioCrd007Module(),
    new BioMob003Module(),
    new BioDoc009Module(),
    new BioDoc010Module(),
    new BioDoc011Module(),
    new BioDev003Module(),
    new BioAtt001Module(),
    new BioRpt001Module(),
    new BioRpt002Module(),
    new BioRpt003Module(),
    new BioVis001Module(),
    new BioAcc003Module(),
    new BioTrn001Module(),
    new BioTrn002Module(),
    new BioMob005Module(),
    new BioAcc002Module(),
    new BioBio008Module(),
    new BioInt001Module(),
    new BioAtt002Module(),
    new BioMob004Module(),
    new BioVis002Module(),
    new BioInt002Module(),
    new BioRpt004Module(),
    new BioBio009Module(),
    new BioInt003Module(),
    new BioTen003Module(),
    new BioInt004Module(),
    new BioAtt003Module(),
    new BioInt005Module(),
    new BioTrn003Module(),
    new DemoSessionIdentitySeedModule(),
    new BioDev004Module(),
    new BioOps005Module(),
});

moduleHost.RegisterServices(builder.Services, builder.Configuration);

var app = builder.Build();

moduleHost.Configure(app);
moduleHost.MapEndpoints(app);

app.Run();

/// <summary>Exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in integration tests.</summary>
public partial class Program
{
}
