namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// BIO-ACC-001-AC01's WHO+CREDENTIAL+WHERE+WHEN+STATUS predicate value types - one small,
/// closed-shape record per dimension, all in this one file since none is more than a
/// discriminated match-kind plus an optional value. Every "Any"-kind match matches
/// unconditionally; every specific-kind match compares by exact value. None of these dimensions
/// has a reachable system of record within this story's declared dependency graph (only
/// BIO-IDN-006 and BIO-ARC-006 - see docs/features/bio-acc-001/README.md "Integration seam"),
/// so - unlike <see cref="IdentityStatus"/>, which BIO-IDN-001's PersonService gateway resolves
/// live - credential class, location and presented card status are supplied directly by the
/// evaluation caller (see <see cref="PolicyEvaluationContext"/>), and subject/profile keys are
/// free-form business strings this project does not validate against another story's catalogue.
/// </summary>

public enum SubjectMatchKind
{
    /// <summary>Matches any subject - the rule applies WHO-independently.</summary>
    AnyPerson,

    /// <summary>Matches exactly one named person (<see cref="SubjectMatch.Value"/> is a canonical <c>per_</c> id, existence-checked at authoring time).</summary>
    SpecificPerson,

    /// <summary>Matches every person who holds the named BIO-IDN-002 profile key (<see cref="SubjectMatch.Value"/> is a free-form business string this project does not validate).</summary>
    ProfileKey,
}

public static class SubjectMatchKindExtensions
{
    public static string ToWireString(this SubjectMatchKind kind) => kind switch
    {
        SubjectMatchKind.AnyPerson => "ANY_PERSON",
        SubjectMatchKind.SpecificPerson => "SPECIFIC_PERSON",
        SubjectMatchKind.ProfileKey => "PROFILE_KEY",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out SubjectMatchKind kind)
    {
        switch (value)
        {
            case "ANY_PERSON": kind = SubjectMatchKind.AnyPerson; return true;
            case "SPECIFIC_PERSON": kind = SubjectMatchKind.SpecificPerson; return true;
            case "PROFILE_KEY": kind = SubjectMatchKind.ProfileKey; return true;
            default: kind = default; return false;
        }
    }
}

public sealed record SubjectMatch(SubjectMatchKind Kind, string? Value)
{
    public static readonly SubjectMatch Any = new(SubjectMatchKind.AnyPerson, null);

    public bool Matches(string personId, string? profileKey) => Kind switch
    {
        SubjectMatchKind.AnyPerson => true,
        SubjectMatchKind.SpecificPerson => string.Equals(Value, personId, StringComparison.Ordinal),
        SubjectMatchKind.ProfileKey => profileKey is not null && string.Equals(Value, profileKey, StringComparison.Ordinal),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "unreachable: exhaustive switch"),
    };
}

public enum CredentialClassMatchKind
{
    Any,
    SpecificClass,
}

public static class CredentialClassMatchKindExtensions
{
    public static string ToWireString(this CredentialClassMatchKind kind) => kind switch
    {
        CredentialClassMatchKind.Any => "ANY",
        CredentialClassMatchKind.SpecificClass => "SPECIFIC_CLASS",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out CredentialClassMatchKind kind)
    {
        switch (value)
        {
            case "ANY": kind = CredentialClassMatchKind.Any; return true;
            case "SPECIFIC_CLASS": kind = CredentialClassMatchKind.SpecificClass; return true;
            default: kind = default; return false;
        }
    }
}

public sealed record CredentialClassMatch(CredentialClassMatchKind Kind, string? CredentialClass)
{
    public static readonly CredentialClassMatch Any = new(CredentialClassMatchKind.Any, null);

    public bool Matches(string presentedCredentialClass) => Kind switch
    {
        CredentialClassMatchKind.Any => true,
        CredentialClassMatchKind.SpecificClass => string.Equals(CredentialClass, presentedCredentialClass, StringComparison.Ordinal),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "unreachable: exhaustive switch"),
    };
}

public enum LocationMatchKind
{
    Any,
    SpecificLocation,
}

public static class LocationMatchKindExtensions
{
    public static string ToWireString(this LocationMatchKind kind) => kind switch
    {
        LocationMatchKind.Any => "ANY",
        LocationMatchKind.SpecificLocation => "SPECIFIC_LOCATION",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out LocationMatchKind kind)
    {
        switch (value)
        {
            case "ANY": kind = LocationMatchKind.Any; return true;
            case "SPECIFIC_LOCATION": kind = LocationMatchKind.SpecificLocation; return true;
            default: kind = default; return false;
        }
    }
}

public sealed record LocationMatch(LocationMatchKind Kind, string? LocationId)
{
    public static readonly LocationMatch Any = new(LocationMatchKind.Any, null);

    public bool Matches(string presentedLocationId) => Kind switch
    {
        LocationMatchKind.Any => true,
        LocationMatchKind.SpecificLocation => string.Equals(LocationId, presentedLocationId, StringComparison.Ordinal),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "unreachable: exhaustive switch"),
    };
}

public enum ScheduleMatchKind
{
    Always,
    Windows,
}

/// <summary>
/// One recurring weekly window, evaluated in UTC (a documented, disclosed simplification - this
/// story does not own a tenant-timezone model). <see cref="StartTime"/> must be strictly before
/// <see cref="EndTime"/> on the same <see cref="DayOfWeek"/>: an overnight-spanning window (e.g.
/// 22:00-06:00) is not supported and must instead be expressed as two windows.
/// </summary>
public sealed record ScheduleWindow(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

public static class ScheduleMatchKindExtensions
{
    public static string ToWireString(this ScheduleMatchKind kind) => kind switch
    {
        ScheduleMatchKind.Always => "ALWAYS",
        ScheduleMatchKind.Windows => "WINDOWS",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out ScheduleMatchKind kind)
    {
        switch (value)
        {
            case "ALWAYS": kind = ScheduleMatchKind.Always; return true;
            case "WINDOWS": kind = ScheduleMatchKind.Windows; return true;
            default: kind = default; return false;
        }
    }
}

public sealed record ScheduleMatch(ScheduleMatchKind Kind, IReadOnlyList<ScheduleWindow> Windows)
{
    public static readonly ScheduleMatch Always = new(ScheduleMatchKind.Always, []);

    public bool Matches(DateTime occurredAtUtc)
    {
        if (Kind == ScheduleMatchKind.Always)
        {
            return true;
        }

        var day = occurredAtUtc.DayOfWeek;
        var time = TimeOnly.FromDateTime(occurredAtUtc);
        return Windows.Any(w => w.DayOfWeek == day && time >= w.StartTime && time < w.EndTime);
    }
}

public sealed record IdentityStatusMatch(bool AnyStatus, IReadOnlySet<IdentityStatus> AllowedStatuses)
{
    public static readonly IdentityStatusMatch Any = new(true, new HashSet<IdentityStatus>());

    public bool Matches(IdentityStatus status) => AnyStatus || AllowedStatuses.Contains(status);
}

public sealed record CardStatusMatch(bool AnyStatus, IReadOnlySet<CardStatus> AllowedStatuses)
{
    public static readonly CardStatusMatch Any = new(true, new HashSet<CardStatus>());

    public bool Matches(CardStatus status) => AnyStatus || AllowedStatuses.Contains(status);
}
