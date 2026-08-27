using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.OrderBooks;

/// <summary>
/// The quantity modeled at one unit price while executing an order-book scenario.
/// </summary>
public sealed record OrderBookExecutionFill(int Quantity, Money UnitPrice, Money TotalValue);
