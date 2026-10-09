using Biolog.Features.NfcReader.Model;

namespace Biolog.Features.NfcReader.Tests.Fixtures;

/// <summary>Test-only synthetic transport (builder constraint: "use synthetic data only") - no
/// real NFC hardware exists in this environment, so <see cref="ChipReadSessionTests"/> drives
/// <see cref="ChipReadSession.Read"/> against this fake rather than any real
/// <see cref="IChipDataGroupTransport"/> implementation.</summary>
public sealed class FakeChipDataGroupTransport : IChipDataGroupTransport
{
    private readonly bool _secureChannelSucceeds;
    private readonly IReadOnlyDictionary<ChipDataGroupId, byte[]> _dataGroups;

    public FakeChipDataGroupTransport(bool secureChannelSucceeds, IReadOnlyDictionary<ChipDataGroupId, byte[]> dataGroups)
    {
        _secureChannelSucceeds = secureChannelSucceeds;
        _dataGroups = dataGroups;
    }

    public NfcAccessKeys? KeysReceived { get; private set; }

    public bool EstablishSecureChannel(NfcAccessKeys keys)
    {
        KeysReceived = keys;
        return _secureChannelSucceeds;
    }

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
