namespace GoymenERP.Api.SalesRuntime;

/// <summary>
/// MOD-07 / SAT-D10: an optional host-supplied source of the business time zone's rules for the sales order PDF (e.g. a
/// pinned tzdata). Not registered by default: the PDF then uses the system's Europe/Istanbul rules
/// (<see cref="SalesOrderPdfComposer.SystemBusinessTimeZone"/>). A source answering null means the rules are unavailable,
/// and nothing with a time is printed (503), never a time in another zone.
/// </summary>
public interface ISalesOrderPdfBusinessTimeZoneSource
{
    TimeZoneInfo? BusinessTimeZone { get; }
}
