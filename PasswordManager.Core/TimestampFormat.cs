using System.Globalization;

namespace PasswordManager.Core;

/// <summary>
/// Renders stored timestamps the way pwman shows them everywhere:
/// "Sept 1st 2026, 11:06 am".
///
/// This lives in Core so the CLI, the desktop GUI and the browser extension's
/// native host all print dates identically instead of each rolling their own
/// format string. (The extension's popup.js mirrors this same format in
/// JavaScript, since it renders entry dates itself.)
///
/// The format is built by hand rather than through a culture-aware format
/// string: .NET has no format specifier for an ordinal day suffix ("1st"),
/// and two of the frontends run with InvariantGlobalization enabled, so
/// relying on culture data would give inconsistent results between them.
/// </summary>
public static class TimestampFormat
{
    private static readonly string[] MonthNames =
    {
        "Jan", "Feb", "Mar", "Apr", "May", "Jun",
        "Jul", "Aug", "Sept", "Oct", "Nov", "Dec",
    };

    /// <summary>
    /// Formats a stored UTC timestamp in the viewer's local time, or
    /// "unknown" for entries saved before pwman started recording dates.
    /// </summary>
    public static string Format(DateTimeOffset? utc)
    {
        if (utc is null)
        {
            return "unknown";
        }

        var local = utc.Value.ToLocalTime();

        // 12-hour clock: midnight and noon are 12, not 0.
        var hour = local.Hour % 12;
        if (hour == 0)
        {
            hour = 12;
        }

        var meridiem = local.Hour < 12 ? "am" : "pm";

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1}{2} {3}, {4}:{5:00} {6}",
            MonthNames[local.Month - 1],
            local.Day,
            OrdinalSuffix(local.Day),
            local.Year,
            hour,
            local.Minute,
            meridiem);
    }

    /// <summary>
    /// The English ordinal suffix for a day of the month. 11th, 12th and 13th
    /// are the exceptions -- they take "th" despite ending in 1, 2 and 3.
    /// </summary>
    public static string OrdinalSuffix(int day)
    {
        if (day is >= 11 and <= 13)
        {
            return "th";
        }

        return (day % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
    }
}
