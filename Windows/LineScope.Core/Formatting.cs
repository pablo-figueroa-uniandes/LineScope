using System.Globalization;

namespace LineScope.Core;

public static class Fmt
{
    /// <summary>Locale-aware number with grouping and at most <paramref name="maxFraction"/> decimals.</summary>
    public static string Number(double v, int maxFraction = 3) =>
        v.ToString(maxFraction > 0 ? "#,0." + new string('#', maxFraction) : "#,0", CultureInfo.CurrentCulture);

    /// <summary>Locale-aware number with exactly <paramref name="digits"/> decimals.</summary>
    public static string Fixed(double v, int digits) =>
        v.ToString("N" + digits, CultureInfo.CurrentCulture);
}
