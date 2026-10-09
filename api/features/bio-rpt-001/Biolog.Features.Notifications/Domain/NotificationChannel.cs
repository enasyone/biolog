using System.Text.Json.Serialization;

namespace Biolog.Features.Notifications;

/// <summary>The channels the user story names verbatim: "email/SMS/push/Teams/webhook and
/// future channels." A closed enum today (BIO-RPT-001-AC02: each gets its own isolated
/// <see cref="INotificationChannelAdapter"/>), extended by adding one more adapter + one
/// more enum value when a real "future channel" is actually needed - never a generic
/// string, which would let a rule reference a channel with no adapter behind it at all.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationChannel>))]
public enum NotificationChannel
{
    Email,
    Sms,
    Push,
    Teams,
    Webhook,
}
