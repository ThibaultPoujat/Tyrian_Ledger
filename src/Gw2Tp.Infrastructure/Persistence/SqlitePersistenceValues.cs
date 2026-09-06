using System.Globalization;
using Gw2Tp.Application.Persistence;

namespace Gw2Tp.Infrastructure.Persistence;

internal static class SqlitePersistenceValues
{
    public static string ToUtcTimestamp(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp values must be UTC.", parameterName);
        }

        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset FromUtcTimestamp(string value, string columnName)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedValue) || parsedValue.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException($"The SQLite {columnName} value is not a UTC round-trip timestamp.");
        }

        return parsedValue;
    }

    public static void ValidateAccountProfile(AccountProfile accountProfile)
    {
        ArgumentNullException.ThrowIfNull(accountProfile);
        if (accountProfile.Id <= 0 || string.IsNullOrWhiteSpace(accountProfile.AccountScopeId))
        {
            throw new ArgumentException("The account profile is invalid.", nameof(accountProfile));
        }
    }

    public static void ValidateSide(PersonalTradingPostSide side, string parameterName)
    {
        if (side is not PersonalTradingPostSide.Buy and not PersonalTradingPostSide.Sell)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public static void ValidateCompletedTransaction(CompletedPersonalTradingPostTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.ExternalTransactionId <= 0 || transaction.ItemId <= 0 ||
            transaction.UnitPriceInCopper < 0 || transaction.Quantity <= 0)
        {
            throw new ArgumentException("The completed transaction contains an invalid identifier, copper value, or quantity.", nameof(transaction));
        }

        ValidateSide(transaction.Side, nameof(transaction.Side));
        _ = ToUtcTimestamp(transaction.CreatedAtUtc, nameof(transaction.CreatedAtUtc));
        _ = ToUtcTimestamp(transaction.CompletedAtUtc, nameof(transaction.CompletedAtUtc));
    }

    public static void ValidateCurrentOrder(CurrentPersonalTradingPostOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.ExternalOrderId <= 0 || order.ItemId <= 0 ||
            order.UnitPriceInCopper < 0 || order.Quantity <= 0)
        {
            throw new ArgumentException("The current order contains an invalid identifier, copper value, or quantity.", nameof(order));
        }

        ValidateSide(order.Side, nameof(order.Side));
        _ = ToUtcTimestamp(order.CreatedAtUtc, nameof(order.CreatedAtUtc));
    }
}
