namespace Biolog.Features.TransportNotifications;

/// <summary>
/// AC01 - the four selectable categories a <see cref="TransportNotificationRule"/> picks from.
/// <see cref="Board"/>/<see cref="Alight"/> map directly onto bio-trn-002's own
/// <c>TransportEventMode</c> for a raw captured event. <see cref="MissedAlight"/> maps onto
/// bio-trn-002's own already-computed <c>DerivedPresenceStatus.MissingAlightException</c> (a
/// person boarded but was never confirmed alighting within the configured window) - read via its
/// own disclosed <c>ITriggeredNotificationLog</c> seam, never re-derived here. <see cref="Exception"/>
/// is this story's own new, additive classification (not present in bio-trn-002 at all): a
/// captured event where NO evidence entry was verified - a genuinely anomalous/low-confidence
/// capture, derived read-only from fields bio-trn-002's own <c>TransportEvent.Evidence</c> already
/// exposes, surfaced for operator awareness without mutating the event itself (AC04).
/// </summary>
public enum TransportNotificationEventType
{
    Board,
    Alight,
    MissedAlight,
    Exception,
}

public static class TransportNotificationEventTypeExtensions
{
    public static string ToWireString(this TransportNotificationEventType type) => type switch
    {
        TransportNotificationEventType.Board => "board",
        TransportNotificationEventType.Alight => "alight",
        TransportNotificationEventType.MissedAlight => "missed_alight",
        TransportNotificationEventType.Exception => "exception",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out TransportNotificationEventType type)
    {
        switch (value)
        {
            case "board": type = TransportNotificationEventType.Board; return true;
            case "alight": type = TransportNotificationEventType.Alight; return true;
            case "missed_alight": type = TransportNotificationEventType.MissedAlight; return true;
            case "exception": type = TransportNotificationEventType.Exception; return true;
            default: type = default; return false;
        }
    }
}
