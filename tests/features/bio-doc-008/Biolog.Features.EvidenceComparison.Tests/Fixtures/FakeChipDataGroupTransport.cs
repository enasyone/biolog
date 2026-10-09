using Biolog.Features.NfcReader.Model;

namespace Biolog.Features.EvidenceComparison.Tests.Fixtures;

/// <summary>Own local copy of BIO-DOC-007's identically named fixture (builder constraint: "use
/// synthetic data only") - no real NFC hardware exists in this environment.</summary>
public sealed class FakeChipDataGroupTransport : IChipDataGroupTransport
{
    private readonly bool _secureChannelSucceeds;
    private readonly IReadOnlyDictionary<ChipDataGroupId, byte[]> _dataGroups;

    public FakeChipDataGroupTransport(bool secureChannelSucceeds, IReadOnlyDictionary<ChipDataGroupId, byte[]> dataGroups)
    {
        _secureChannelSucceeds = secureChannelSucceeds;
        _dataGroups = dataGroups;
    }

    public bool EstablishSecureChannel(NfcAccessKeys keys) => _secureChannelSucceeds;

    public bool TryReadDataGroup(ChipDataGroupId id, out byte[]? rawBytes)
    {
        if (_dataGroups.TryGetValue(id, out var bytes))
        {
            rawBytes = bytes;
            return true;
        }

        rawBytes = null;
        return false;
    }
}
