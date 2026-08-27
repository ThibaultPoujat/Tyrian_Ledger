using Gw2Tp.Application.Preferences;

namespace Gw2Tp.Web.Preferences;

internal static class UserSessionPreferencesEndpoints
{
    public static IEndpointRouteBuilder MapUserSessionPreferencesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/preferences/user-session", async (
            IUserSessionPreferencesStore preferencesStore,
            CancellationToken cancellationToken) =>
        {
            var preferences = await preferencesStore.GetAsync(cancellationToken);
            return Results.Ok(UserSessionPreferencesResponse.From(preferences));
        });
        endpoints.MapPut("/api/preferences/user-session", async (
            UpdateUserSessionPreferencesRequest request,
            IUserSessionPreferencesStore preferencesStore,
            CancellationToken cancellationToken) =>
        {
            if (!request.TryCreatePreferences(out var preferences, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            await preferencesStore.SaveAsync(preferences, cancellationToken);
            return Results.Ok(UserSessionPreferencesResponse.From(preferences));
        });

        return endpoints;
    }

    private sealed record UserSessionPreferencesResponse(
        long? CapitalLimitCopper,
        long? MinimumProfitCopper,
        string RiskPreference,
        string StrategyPreference,
        int AllocationPercent)
    {
        public static UserSessionPreferencesResponse From(UserSessionPreferences preferences)
        {
            ArgumentNullException.ThrowIfNull(preferences);

            return new UserSessionPreferencesResponse(
                preferences.CapitalLimitCopper,
                preferences.MinimumProfitCopper,
                ToResponseValue(preferences.RiskPreference),
                ToResponseValue(preferences.StrategyPreference),
                preferences.AllocationPercent);
        }

        private static string ToResponseValue(OpportunityRiskPreference preference) => preference switch
        {
            OpportunityRiskPreference.All => "all",
            OpportunityRiskPreference.Normal => "normal",
            OpportunityRiskPreference.Reduced => "reduced",
            _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, "The risk preference is not supported."),
        };

        private static string ToResponseValue(OpportunityStrategyPreference preference) => preference switch
        {
            OpportunityStrategyPreference.All => "all",
            OpportunityStrategyPreference.MarketFlip => "market-flip",
            _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, "The strategy preference is not supported."),
        };
    }

    private sealed record UpdateUserSessionPreferencesRequest(
        long? CapitalLimitCopper,
        long? MinimumProfitCopper,
        string? RiskPreference,
        string? StrategyPreference,
        int AllocationPercent)
    {
        public bool TryCreatePreferences(
            out UserSessionPreferences preferences,
            out Dictionary<string, string[]> errors)
        {
            errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            ValidateCopper(CapitalLimitCopper, "capitalLimitCopper", errors);
            ValidateCopper(MinimumProfitCopper, "minimumProfitCopper", errors);

            var riskPreference = ParseRiskPreference(RiskPreference, errors);
            var strategyPreference = ParseStrategyPreference(StrategyPreference, errors);

            if (AllocationPercent is < UserSessionPreferences.MinimumAllocationPercent or
                > UserSessionPreferences.MaximumAllocationPercent)
            {
                errors["allocationPercent"] = ["Allocation percent must be an integer from 1 through 100."];
            }

            if (errors.Count > 0 || riskPreference is null || strategyPreference is null)
            {
                preferences = UserSessionPreferences.Default;
                return false;
            }

            preferences = UserSessionPreferences.Create(
                CapitalLimitCopper,
                MinimumProfitCopper,
                riskPreference.Value,
                strategyPreference.Value,
                AllocationPercent);
            return true;
        }

        private static void ValidateCopper(
            long? value,
            string propertyName,
            IDictionary<string, string[]> errors)
        {
            if (value is < 0 or > UserSessionPreferences.MaximumSafeIntegerCopper)
            {
                errors[propertyName] = ["Copper values must be non-negative JavaScript-safe integers."];
            }
        }

        private static OpportunityRiskPreference? ParseRiskPreference(
            string? value,
            IDictionary<string, string[]> errors) => value switch
        {
            "all" => OpportunityRiskPreference.All,
            "normal" => OpportunityRiskPreference.Normal,
            "reduced" => OpportunityRiskPreference.Reduced,
            _ => InvalidRiskPreference(errors),
        };

        private static OpportunityStrategyPreference? ParseStrategyPreference(
            string? value,
            IDictionary<string, string[]> errors) => value switch
        {
            "all" => OpportunityStrategyPreference.All,
            "market-flip" => OpportunityStrategyPreference.MarketFlip,
            _ => InvalidStrategyPreference(errors),
        };

        private static OpportunityRiskPreference? InvalidRiskPreference(IDictionary<string, string[]> errors)
        {
            errors["riskPreference"] = ["Risk preference must be all, normal, or reduced."];
            return null;
        }

        private static OpportunityStrategyPreference? InvalidStrategyPreference(IDictionary<string, string[]> errors)
        {
            errors["strategyPreference"] = ["Strategy preference must be all or market-flip."];
            return null;
        }
    }
}
