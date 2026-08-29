using Gw2Tp.Analytics.Finance;
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
        int AllocationPercent,
        int AnalysisQuantity,
        int? ListingFeeBasisPoints,
        string? ListingFeeRounding,
        int? ExchangeFeeBasisPoints,
        string? ExchangeFeeRounding)
    {
        public static UserSessionPreferencesResponse From(UserSessionPreferences preferences)
        {
            ArgumentNullException.ThrowIfNull(preferences);

            return new UserSessionPreferencesResponse(
                preferences.CapitalLimitCopper,
                preferences.MinimumProfitCopper,
                ToResponseValue(preferences.RiskPreference),
                ToResponseValue(preferences.StrategyPreference),
                preferences.AllocationPercent,
                preferences.AnalysisQuantity,
                preferences.ListingFeeBasisPoints,
                ToResponseValue(preferences.ListingFeeRounding),
                preferences.ExchangeFeeBasisPoints,
                ToResponseValue(preferences.ExchangeFeeRounding));
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

        private static string? ToResponseValue(FeeRounding? rounding) => rounding switch
        {
            null => null,
            FeeRounding.Down => "down",
            FeeRounding.Up => "up",
            _ => throw new ArgumentOutOfRangeException(nameof(rounding), rounding, "The fee rounding mode is not supported."),
        };
    }

    private sealed record UpdateUserSessionPreferencesRequest(
        long? CapitalLimitCopper,
        long? MinimumProfitCopper,
        string? RiskPreference,
        string? StrategyPreference,
        int AllocationPercent,
        int? AnalysisQuantity,
        int? ListingFeeBasisPoints,
        string? ListingFeeRounding,
        int? ExchangeFeeBasisPoints,
        string? ExchangeFeeRounding)
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
            var listingFeeRounding = ParseFeeRounding(ListingFeeRounding, "listingFeeRounding", errors);
            var exchangeFeeRounding = ParseFeeRounding(ExchangeFeeRounding, "exchangeFeeRounding", errors);
            var analysisQuantity = AnalysisQuantity ?? UserSessionPreferences.DefaultAnalysisQuantity;

            if (AllocationPercent is < UserSessionPreferences.MinimumAllocationPercent or
                > UserSessionPreferences.MaximumAllocationPercent)
            {
                errors["allocationPercent"] = ["Allocation percent must be an integer from 1 through 100."];
            }

            if (analysisQuantity <= 0)
            {
                errors["analysisQuantity"] = ["Analysis quantity must be a positive whole number."];
            }

            var feeValues = new object?[]
            {
                ListingFeeBasisPoints,
                listingFeeRounding,
                ExchangeFeeBasisPoints,
                exchangeFeeRounding,
            };
            if (!feeValues.All(value => value is null) && feeValues.Any(value => value is null))
            {
                errors["fees"] = ["Listing and exchange fee rules must both be fully configured or both be blank."];
            }

            ValidateFeeBasisPoints(ListingFeeBasisPoints, "listingFeeBasisPoints", errors);
            ValidateFeeBasisPoints(ExchangeFeeBasisPoints, "exchangeFeeBasisPoints", errors);

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
                AllocationPercent,
                analysisQuantity,
                ListingFeeBasisPoints,
                listingFeeRounding,
                ExchangeFeeBasisPoints,
                exchangeFeeRounding);
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

        private static FeeRounding? ParseFeeRounding(
            string? value,
            string propertyName,
            IDictionary<string, string[]> errors) => value switch
        {
            null => null,
            "down" => FeeRounding.Down,
            "up" => FeeRounding.Up,
            _ => InvalidFeeRounding(propertyName, errors),
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

        private static FeeRounding? InvalidFeeRounding(
            string propertyName,
            IDictionary<string, string[]> errors)
        {
            errors[propertyName] = ["Fee rounding must be down or up when a fee rule is configured."];
            return null;
        }

        private static void ValidateFeeBasisPoints(
            int? value,
            string propertyName,
            IDictionary<string, string[]> errors)
        {
            if (value is < 0 or > FeeRule.BasisPointsPerWhole)
            {
                errors[propertyName] = [$"Fee basis points must be an integer from 0 through {FeeRule.BasisPointsPerWhole}."];
            }
        }
    }
}
