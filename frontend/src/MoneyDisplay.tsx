type Money = { copper: string };

type MoneyDisplayProps = {
  money: Money | null;
  unavailableLabel?: string;
  compact?: boolean;
  className?: string;
};

export default function MoneyDisplay({
  money,
  unavailableLabel = 'Indisponible',
  compact = false,
  className = '',
}: MoneyDisplayProps) {
  if (money === null) {
    return <span className={className}>{unavailableLabel}</span>;
  }

  const value = BigInt(money.copper);
  const negative = value < 0n;
  const absolute = negative ? -value : value;
  const gold = absolute / 10000n;
  const silver = (absolute % 10000n) / 100n;
  const copper = absolute % 100n;
  const spoken = [
    gold > 0n ? `${gold} pièce${gold === 1n ? '' : 's'} d'or` : null,
    silver > 0n ? `${silver} pièce${silver === 1n ? '' : 's'} d'argent` : null,
    copper > 0n || (gold === 0n && silver === 0n) ? `${copper} pièce${copper === 1n ? '' : 's'} de cuivre` : null,
  ].filter(Boolean).join(', ');

  const values = compact
    ? [
        gold > 0n ? ['gold', gold] as const : null,
        silver > 0n ? ['silver', silver] as const : null,
        copper > 0n || (gold === 0n && silver === 0n) ? ['copper', copper] as const : null,
      ].filter((part): part is readonly ['gold' | 'silver' | 'copper', bigint] => part !== null)
    : [
        ['gold', gold] as const,
        ['silver', silver] as const,
        ['copper', copper] as const,
      ];

  return (
    <span
      aria-label={`${negative ? 'moins ' : ''}${spoken}`}
      className={`money-display ${className}`.trim()}
      role="img"
    >
      {negative && <span aria-hidden="true" className="money-sign">−</span>}
      {values.map(([denomination, amount]) => (
        <span aria-hidden="true" className="money-part" key={denomination}>
          <span className="money-value">{amount.toString()}</span>
          <span className={`coin coin--${denomination}`} />
        </span>
      ))}
    </span>
  );
}
