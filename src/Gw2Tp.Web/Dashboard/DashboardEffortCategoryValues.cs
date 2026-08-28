using Gw2Tp.Application.SessionPlanning;

namespace Gw2Tp.Web.Dashboard;

internal static class DashboardEffortCategoryValues
{
    public static bool TryParse(string? value, out SessionEffortCategory? effortCategory)
    {
        effortCategory = value switch
        {
            null or "" => null,
            "very-low" => SessionEffortCategory.VeryLow,
            "low" => SessionEffortCategory.Low,
            "medium" => SessionEffortCategory.Medium,
            "high" => SessionEffortCategory.High,
            "ongoing-patient" => SessionEffortCategory.OngoingPatient,
            _ => null,
        };

        return value is null or "" || effortCategory is not null;
    }

    public static string ToResponseValue(SessionEffortCategory effortCategory) => effortCategory switch
    {
        SessionEffortCategory.VeryLow => "very-low",
        SessionEffortCategory.Low => "low",
        SessionEffortCategory.Medium => "medium",
        SessionEffortCategory.High => "high",
        SessionEffortCategory.OngoingPatient => "ongoing-patient",
        _ => throw new ArgumentOutOfRangeException(nameof(effortCategory), effortCategory, "The effort category is not supported."),
    };
}
