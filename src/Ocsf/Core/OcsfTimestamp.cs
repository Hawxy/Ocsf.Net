namespace Ocsf;

/// <summary>
/// An OCSF timestamp: the number of milliseconds since the Unix epoch (1970-01-01T00:00:00Z).
/// Serializes as a JSON number, matching the OCSF <c>timestamp_t</c> wire format.
/// </summary>
public readonly struct OcsfTimestamp : IEquatable<OcsfTimestamp>, IComparable<OcsfTimestamp>
{
    public OcsfTimestamp(long epochMilliseconds) => EpochMilliseconds = epochMilliseconds;

    /// <summary>Milliseconds since the Unix epoch.</summary>
    public long EpochMilliseconds { get; }

    /// <summary>The current UTC time.</summary>
    public static OcsfTimestamp Now => FromDateTimeOffset(DateTimeOffset.UtcNow);

    public static OcsfTimestamp FromDateTimeOffset(DateTimeOffset value) => new(value.ToUnixTimeMilliseconds());

    /// <summary>Converts to a <see cref="DateTimeOffset"/> with UTC offset.
    /// Throws if the value is outside the <see cref="DateTimeOffset"/> range.</summary>
    public DateTimeOffset ToDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds(EpochMilliseconds);

    public static implicit operator OcsfTimestamp(long epochMilliseconds) => new(epochMilliseconds);

    public static implicit operator long(OcsfTimestamp value) => value.EpochMilliseconds;

    public static implicit operator OcsfTimestamp(DateTimeOffset value) => FromDateTimeOffset(value);

    public static implicit operator DateTimeOffset(OcsfTimestamp value) => value.ToDateTimeOffset();

    public bool Equals(OcsfTimestamp other) => EpochMilliseconds == other.EpochMilliseconds;

    public override bool Equals(object? obj) => obj is OcsfTimestamp other && Equals(other);

    public override int GetHashCode() => EpochMilliseconds.GetHashCode();

    public int CompareTo(OcsfTimestamp other) => EpochMilliseconds.CompareTo(other.EpochMilliseconds);

    public static bool operator ==(OcsfTimestamp left, OcsfTimestamp right) => left.Equals(right);

    public static bool operator !=(OcsfTimestamp left, OcsfTimestamp right) => !left.Equals(right);

    public static bool operator <(OcsfTimestamp left, OcsfTimestamp right) => left.CompareTo(right) < 0;

    public static bool operator >(OcsfTimestamp left, OcsfTimestamp right) => left.CompareTo(right) > 0;

    public static bool operator <=(OcsfTimestamp left, OcsfTimestamp right) => left.CompareTo(right) <= 0;

    public static bool operator >=(OcsfTimestamp left, OcsfTimestamp right) => left.CompareTo(right) >= 0;

    public override string ToString() => EpochMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
