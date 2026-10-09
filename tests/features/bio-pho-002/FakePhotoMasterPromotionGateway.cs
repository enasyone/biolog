using Biolog.Features.PhotoBooth.Audit;
using Biolog.Features.PhotoBooth.Domain;

namespace Biolog.Features.PhotoBooth.Tests;

/// <summary>Test double for <see cref="IPhotoMasterPromotionGateway"/> - records every promotion call without touching a real BIO-PHO-001 PhotoMasterService, since this project has no ProjectReference to that one.</summary>
public sealed class FakePhotoMasterPromotionGateway : IPhotoMasterPromotionGateway
{
    private int _nextVersion = 1;
    public List<(string PersonId, byte[] Content, AuditActor Actor, string CorrelationId)> Promotions { get; } = [];

    public Task<int> PromoteAsync(string personId, byte[] content, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        Promotions.Add((personId, content, actor, correlationId));
        return Task.FromResult(_nextVersion++);
    }
}
