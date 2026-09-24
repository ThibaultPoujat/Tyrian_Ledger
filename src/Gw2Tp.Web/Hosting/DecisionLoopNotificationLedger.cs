namespace Gw2Tp.Web.Hosting;

internal sealed record NotificationLedgerObservation(
    IReadOnlyList<DecisionLoopNotification> Pending,
    IReadOnlyList<DecisionLoopNotification> NewlyActionable);

/// <summary>
/// Keeps notification lifecycle state separate from recommendation generation.
/// Keys are account-scoped and fingerprints contain only structured execution
/// parameters, never localized display text.
/// </summary>
internal sealed class DecisionLoopNotificationLedger
{
    private readonly Dictionary<string, AccountState> accounts = new(StringComparer.Ordinal);
    private readonly object gate = new();

    internal DecisionLoopPreferences GetPreferences(string accountScopeId)
    {
        lock (gate) return new DecisionLoopPreferences(GetAccount(accountScopeId).Enabled);
    }

    internal void SetEnabled(string accountScopeId, bool enabled)
    {
        lock (gate)
        {
            var account = GetAccount(accountScopeId);
            account.Enabled = enabled;
            if (enabled)
            {
                foreach (var entry in account.Entries.Values.Where(value => value.IsActive && !value.Acknowledged))
                    entry.NeedsDelivery = true;
            }
        }
    }

    internal bool Acknowledge(string accountScopeId, string notificationId)
    {
        lock (gate)
        {
            var account = GetAccount(accountScopeId);
            var entry = account.Entries.Values.FirstOrDefault(value => value.Notification.Id == notificationId && value.IsActive);
            if (entry is null) return false;
            entry.Acknowledged = true;
            entry.NeedsDelivery = false;
            return true;
        }
    }

    internal NotificationLedgerObservation Observe(
        string accountScopeId,
        IReadOnlyCollection<DecisionLoopNotification> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        lock (gate)
        {
            var account = GetAccount(accountScopeId);
            var currentByIdentity = current.ToDictionary(value => value.Identity, StringComparer.Ordinal);
            var newlyActionable = new List<DecisionLoopNotification>();
            foreach (var entry in account.Entries.Values)
            {
                if (!currentByIdentity.TryGetValue(entry.Notification.Identity, out var next))
                {
                    entry.IsActive = false;
                    entry.Acknowledged = false;
                    entry.NeedsDelivery = false;
                    continue;
                }

                if (!string.Equals(entry.Notification.Fingerprint, next.Fingerprint, StringComparison.Ordinal) || !entry.IsActive)
                {
                    entry.Notification = next;
                    entry.Acknowledged = false;
                    entry.NeedsDelivery = true;
                }
                else
                {
                    entry.Notification = entry.Notification with { CreatedAtUtc = next.CreatedAtUtc };
                }

                entry.IsActive = true;
                if (account.Enabled && entry.NeedsDelivery && !entry.Acknowledged)
                {
                    newlyActionable.Add(entry.Notification);
                    entry.NeedsDelivery = false;
                }
            }

            foreach (var next in current.Where(value => !account.Entries.ContainsKey(value.Identity)))
            {
                var entry = new Entry(next, true, false, true);
                account.Entries[next.Identity] = entry;
                if (account.Enabled)
                {
                    newlyActionable.Add(next);
                    entry.NeedsDelivery = false;
                }
            }

            return new NotificationLedgerObservation(Pending(account), newlyActionable);
        }
    }

    internal IReadOnlyList<DecisionLoopNotification> Pending(string accountScopeId)
    {
        lock (gate) return Pending(GetAccount(accountScopeId));
    }

    private static IReadOnlyList<DecisionLoopNotification> Pending(AccountState account) => account.Enabled
        ? account.Entries.Values.Where(value => value.IsActive && !value.Acknowledged)
            .Select(value => value.Notification)
            .OrderByDescending(value => value.IsUrgent)
            .ThenBy(value => value.CreatedAtUtc)
            .ToArray()
        : [];

    private AccountState GetAccount(string accountScopeId)
    {
        if (string.IsNullOrWhiteSpace(accountScopeId)) throw new ArgumentException("An account scope is required.", nameof(accountScopeId));
        if (!accounts.TryGetValue(accountScopeId, out var account))
        {
            account = new AccountState();
            accounts.Add(accountScopeId, account);
        }

        return account;
    }

    private sealed class AccountState
    {
        internal bool Enabled { get; set; } = true;
        internal Dictionary<string, Entry> Entries { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Entry(DecisionLoopNotification notification, bool isActive, bool acknowledged, bool needsDelivery)
    {
        internal DecisionLoopNotification Notification { get; set; } = notification;
        internal bool IsActive { get; set; } = isActive;
        internal bool Acknowledged { get; set; } = acknowledged;
        internal bool NeedsDelivery { get; set; } = needsDelivery;
    }
}
