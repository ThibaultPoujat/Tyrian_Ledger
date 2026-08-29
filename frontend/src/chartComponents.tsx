import type {
  DashboardExecution,
  DashboardFinancials,
  DashboardOpportunity,
  MarketResearchLiquidity,
  MarketResearchPriceStatistics,
  MarketResearchWatchlistItem,
  OperationHistoryStatistics,
} from './dashboardApi';

const chartWidth = 720;
const chartHeight = 320;

export function OpportunityLandscape({ opportunities }: { opportunities: DashboardOpportunity[] }) {
  if (opportunities.length === 0) {
    return <p className="chart-empty">No modeled opportunities are available for the landscape.</p>;
  }

  const margin = { top: 24, right: 28, bottom: 52, left: 76 };
  const plotWidth = chartWidth - margin.left - margin.right;
  const plotHeight = chartHeight - margin.top - margin.bottom;
  const maxCapital = Math.max(...opportunities.map((opportunity) => opportunity.capitalRequiredCopper), 1);
  const maxProfit = Math.max(...opportunities.map((opportunity) => opportunity.modeledNetProfitCopper), 1);
  const maxImpact = Math.max(...opportunities.map((opportunity) => opportunity.liquidityPriceImpactCopper), 1);

  const x = (capital: number) => margin.left + (capital / maxCapital) * plotWidth;
  const y = (profit: number) => margin.top + plotHeight - (profit / maxProfit) * plotHeight;
  const radius = (impact: number) => 7 + (impact / maxImpact) * 8;

  return (
    <div className="chart-card" data-testid="opportunity-landscape">
      <div className="chart-card-heading">
        <div>
          <p className="eyebrow">Orientation view</p>
          <h3>Opportunity landscape</h3>
        </div>
        <p className="chart-caption">Capital required versus modeled profit</p>
      </div>
      <svg
        aria-labelledby="opportunity-landscape-title opportunity-landscape-description"
        className="chart-svg"
        role="img"
        viewBox={`0 0 ${chartWidth} ${chartHeight}`}
      >
        <title id="opportunity-landscape-title">Modeled opportunity landscape</title>
        <desc id="opportunity-landscape-description">
          Each point is a tracked market item. Horizontal position represents capital required,
          vertical position represents modeled net profit, and point size represents modeled price impact.
        </desc>
        <line className="chart-axis" x1={margin.left} x2={margin.left} y1={margin.top} y2={margin.top + plotHeight} />
        <line className="chart-axis" x1={margin.left} x2={margin.left + plotWidth} y1={margin.top + plotHeight} y2={margin.top + plotHeight} />
        <line className="chart-gridline" x1={margin.left} x2={margin.left + plotWidth} y1={margin.top + plotHeight / 2} y2={margin.top + plotHeight / 2} />
        <line className="chart-gridline" x1={margin.left + plotWidth / 2} x2={margin.left + plotWidth / 2} y1={margin.top} y2={margin.top + plotHeight} />
        <text className="chart-axis-label" textAnchor="middle" x={margin.left + plotWidth / 2} y={chartHeight - 12}>
          Capital required
        </text>
        <text
          className="chart-axis-label"
          textAnchor="middle"
          transform={`translate(17 ${margin.top + plotHeight / 2}) rotate(-90)`}
        >
          Modeled profit
        </text>
        <text className="chart-tick-label" textAnchor="middle" x={margin.left} y={chartHeight - 30}>0g</text>
        <text className="chart-tick-label" textAnchor="middle" x={margin.left + plotWidth} y={chartHeight - 30}>{formatCopper(maxCapital)}</text>
        <text className="chart-tick-label" textAnchor="end" x={margin.left - 10} y={margin.top + plotHeight + 4}>0g</text>
        <text className="chart-tick-label" textAnchor="end" x={margin.left - 10} y={margin.top + 4}>{formatCopper(maxProfit)}</text>
        {opportunities.map((opportunity) => (
          <g key={opportunity.itemId}>
            <circle
              aria-label={`${opportunity.label}: ${formatCopper(opportunity.modeledNetProfitCopper)} modeled profit from ${formatCopper(opportunity.capitalRequiredCopper)} capital`}
              className={`landscape-point landscape-${opportunity.confidence}`}
              cx={x(opportunity.capitalRequiredCopper)}
              cy={y(opportunity.modeledNetProfitCopper)}
              r={radius(opportunity.liquidityPriceImpactCopper)}
              tabIndex={0}
            >
              <title>{`${opportunity.label}: ${formatCopper(opportunity.modeledNetProfitCopper)} modeled profit, ${formatCopper(opportunity.capitalRequiredCopper)} capital`}</title>
            </circle>
            <text
              className="chart-point-label"
              x={Math.min(x(opportunity.capitalRequiredCopper) + 10, chartWidth - 110)}
              y={y(opportunity.modeledNetProfitCopper) - 10}
            >
              #{opportunity.rank}
            </text>
          </g>
        ))}
      </svg>
      <div className="chart-legend" aria-label="Opportunity landscape legend">
        <span><i className="legend-dot legend-normal" /> Normal confidence</span>
        <span><i className="legend-dot legend-reduced" /> Reduced confidence</span>
        <span>Point size: modeled price impact</span>
      </div>
      <details className="chart-data-details">
        <summary>View landscape data table</summary>
        <table className="chart-data-table">
          <caption>Modeled opportunity landscape data</caption>
          <thead>
            <tr><th scope="col">Rank</th><th scope="col">Opportunity</th><th scope="col">Capital</th><th scope="col">Modeled profit</th><th scope="col">Price impact</th></tr>
          </thead>
          <tbody>
            {opportunities.map((opportunity) => (
              <tr key={opportunity.itemId}>
                <td>#{opportunity.rank}</td>
                <th scope="row">{opportunity.label}</th>
                <td>{formatCopper(opportunity.capitalRequiredCopper)}</td>
                <td>{formatCopper(opportunity.modeledNetProfitCopper)}</td>
                <td>{formatCopper(opportunity.liquidityPriceImpactCopper)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </div>
  );
}

export function ProfitWaterfall({ financials, listingFeeCopper, exchangeFeeCopper }: {
  financials: DashboardFinancials;
  listingFeeCopper: number;
  exchangeFeeCopper: number;
}) {
  const steps = [
    { label: 'Acquisition', change: -financials.acquisitionCostCopper },
    { label: 'Listing fee', change: -listingFeeCopper },
    { label: 'Gross sale', change: financials.grossSaleValueCopper },
    { label: 'Exchange fee', change: -exchangeFeeCopper },
    { label: 'Modeled profit', change: financials.modeledNetProfitCopper, final: true },
  ];
  let runningTotal = 0;
  const plottedSteps = steps.map((step) => {
    runningTotal = step.final ? financials.modeledNetProfitCopper : runningTotal + step.change;
    return { ...step, total: runningTotal };
  });
  const minValue = Math.min(0, ...plottedSteps.map((step) => Math.min(step.total, step.total - (step.final ? 0 : step.change))));
  const maxValue = Math.max(1, ...plottedSteps.map((step) => Math.max(step.total, step.total - (step.final ? 0 : step.change))));
  const margin = { top: 24, right: 16, bottom: 66, left: 54 };
  const plotWidth = chartWidth - margin.left - margin.right;
  const plotHeight = chartHeight - margin.top - margin.bottom;
  const y = (value: number) => margin.top + ((maxValue - value) / (maxValue - minValue)) * plotHeight;
  const barWidth = Math.min(74, plotWidth / plottedSteps.length - 18);
  const x = (index: number) => margin.left + (index + 0.5) * (plotWidth / plottedSteps.length) - barWidth / 2;

  return (
    <div className="chart-card" data-testid="profit-waterfall">
      <div className="chart-card-heading">
        <div>
          <p className="eyebrow">Calculation bridge</p>
          <h3>Modeled profit waterfall</h3>
        </div>
        <p className="chart-caption">Fees and order-book costs remain visible</p>
      </div>
      <svg
        aria-labelledby="profit-waterfall-title profit-waterfall-description"
        className="chart-svg"
        role="img"
        viewBox={`0 0 ${chartWidth} ${chartHeight}`}
      >
        <title id="profit-waterfall-title">Modeled profit waterfall</title>
        <desc id="profit-waterfall-description">A modeled calculation bridge from acquisition cost to modeled profit.</desc>
        <line className="chart-axis" x1={margin.left} x2={chartWidth - margin.right} y1={y(0)} y2={y(0)} />
        <line className="chart-gridline" x1={margin.left} x2={chartWidth - margin.right} y1={y(maxValue)} y2={y(maxValue)} />
        <line className="chart-gridline" x1={margin.left} x2={chartWidth - margin.right} y1={y(minValue)} y2={y(minValue)} />
        <text className="chart-tick-label" textAnchor="end" x={margin.left - 8} y={y(0) + 4}>0g</text>
        <text className="chart-tick-label" textAnchor="end" x={margin.left - 8} y={y(maxValue) + 4}>{formatCopper(maxValue)}</text>
        {plottedSteps.map((step, index) => {
          const previousTotal = step.final ? 0 : step.total - step.change;
          const upper = Math.max(previousTotal, step.total);
          const lower = Math.min(previousTotal, step.total);
          return (
            <g key={step.label}>
              <rect
                className={`waterfall-bar ${step.change >= 0 ? 'waterfall-positive' : 'waterfall-negative'} ${step.final ? 'waterfall-final' : ''}`}
                height={Math.max(2, y(lower) - y(upper))}
                rx="4"
                width={barWidth}
                x={x(index)}
                y={y(upper)}
              >
                <title>{`${step.label}: ${formatCopper(step.change)}; running total ${formatCopper(step.total)}`}</title>
              </rect>
              {index < plottedSteps.length - 1 && (
                <line className="waterfall-connector" x1={x(index) + barWidth} x2={x(index + 1)} y1={y(step.total)} y2={y(step.total)} />
              )}
              <text className="chart-point-label" textAnchor="middle" x={x(index) + barWidth / 2} y={chartHeight - 38}>
                {step.label}
              </text>
            </g>
          );
        })}
      </svg>
      <details className="chart-data-details">
        <summary>View waterfall data table</summary>
        <table className="chart-data-table">
          <caption>Modeled profit waterfall data</caption>
          <thead><tr><th scope="col">Step</th><th scope="col">Change</th><th scope="col">Running total</th></tr></thead>
          <tbody>{plottedSteps.map((step) => <tr key={step.label}><th scope="row">{step.label}</th><td>{formatCopper(step.change)}</td><td>{formatCopper(step.total)}</td></tr>)}</tbody>
        </table>
      </details>
    </div>
  );
}

export function ExecutionDepthChart({ acquisition, exit }: { acquisition: DashboardExecution; exit: DashboardExecution }) {
  const maxQuantity = Math.max(acquisition.requestedQuantity, exit.requestedQuantity, 1);
  const width = 520;
  const height = 180;
  const barWidth = width - 210;
  const row = (label: string, execution: DashboardExecution, y: number, colorClass: string) => {
    const filledWidth = (execution.filledQuantity / maxQuantity) * barWidth;
    return (
      <g key={label}>
        <text className="chart-point-label" x={0} y={y + 16}>{label}</text>
        <rect className="depth-track" height="22" rx="4" width={barWidth} x={150} y={y} />
        <rect className={`depth-fill ${colorClass}`} height="22" rx="4" width={filledWidth} x={150} y={y}>
          <title>{`${label}: ${execution.filledQuantity} of ${execution.requestedQuantity} modeled items`}</title>
        </rect>
        <text className="chart-tick-label" x={160 + barWidth} y={y + 16}>{execution.filledQuantity}/{execution.requestedQuantity}</text>
      </g>
    );
  };

  return (
    <div className="chart-card depth-chart" data-testid="execution-depth-chart">
      <div className="chart-card-heading">
        <div>
          <p className="eyebrow">Order-book scenario</p>
          <h3>Modeled execution depth</h3>
        </div>
        <p className="chart-caption">Filled quantity at the requested size</p>
      </div>
      <svg aria-labelledby="execution-depth-title execution-depth-description" className="chart-svg" role="img" viewBox={`0 0 ${width} ${height}`}>
        <title id="execution-depth-title">Modeled execution depth</title>
        <desc id="execution-depth-description">A summary of modeled acquisition and exit quantity. This is a scenario, not a guaranteed fill.</desc>
        {row('Acquire', acquisition, 30, 'depth-acquire')}
        {row('Exit', exit, 92, 'depth-exit')}
      </svg>
      <p className="chart-note">This view summarizes the supplied snapshot; it is not a probability of execution.</p>
      <details className="chart-data-details">
        <summary>View execution data table</summary>
        <table className="chart-data-table">
          <caption>Modeled execution depth data</caption>
          <thead><tr><th scope="col">Leg</th><th scope="col">Filled</th><th scope="col">Requested</th><th scope="col">Price impact</th></tr></thead>
          <tbody>
            <tr><th scope="row">Acquisition</th><td>{acquisition.filledQuantity}</td><td>{acquisition.requestedQuantity}</td><td>{formatCopper(acquisition.priceImpactCopper)}</td></tr>
            <tr><th scope="row">Exit</th><td>{exit.filledQuantity}</td><td>{exit.requestedQuantity}</td><td>{formatCopper(exit.priceImpactCopper)}</td></tr>
          </tbody>
        </table>
      </details>
    </div>
  );
}

export function HistoryLifecycleChart({ statistics }: { statistics: OperationHistoryStatistics }) {
  const active = Math.max(0, statistics.operationCount - statistics.lifecycle.terminalOperationCount);
  const segments = [
    { label: 'Completed', value: statistics.lifecycle.completedOperationCount, className: 'lifecycle-completed' },
    { label: 'Cancelled', value: statistics.lifecycle.cancelledOperationCount, className: 'lifecycle-cancelled' },
    { label: 'Active', value: active, className: 'lifecycle-active' },
  ];
  const total = Math.max(statistics.operationCount, 1);

  return (
    <div className="chart-card lifecycle-chart" data-testid="history-lifecycle-chart">
      <div className="chart-card-heading"><div><p className="eyebrow">Operation status</p><h3>Lifecycle mix</h3></div><p className="chart-caption">Recorded locally</p></div>
      <div className="lifecycle-bar" role="img" aria-label={segments.map((segment) => `${segment.label}: ${segment.value}`).join(', ')}>
        {segments.map((segment) => {
          const width = (segment.value / total) * 100;
          const element = <span className={segment.className} key={segment.label} style={{ width: `${width}%` }} title={`${segment.label}: ${segment.value}`} />;
          return element;
        })}
      </div>
      <div className="chart-legend lifecycle-legend">
        {segments.map((segment) => <span key={segment.label}><i className={`legend-square ${segment.className}`} /> {segment.label}: {segment.value}</span>)}
      </div>
      <table className="chart-data-table chart-data-table-inline"><caption>Operation lifecycle data</caption><tbody>{segments.map((segment) => <tr key={segment.label}><th scope="row">{segment.label}</th><td>{segment.value}</td></tr>)}</tbody></table>
    </div>
  );
}

export function ResearchBandChart({ items }: { items: MarketResearchWatchlistItem[] }) {
  const eligibleItems = items.filter((item) => hasPriceBand(item.buyPrices) || hasPriceBand(item.sellPrices));
  if (eligibleItems.length === 0) {
    return <p className="chart-empty">Observed price bands will appear after enough local samples are collected.</p>;
  }

  const width = 720;
  const rowHeight = 58;
  const height = Math.max(170, eligibleItems.length * rowHeight + 60);
  const margin = { top: 26, right: 34, bottom: 20, left: 150 };
  const maxValue = Math.max(...eligibleItems.flatMap((item) => [item.buyPrices.ninetiethPercentileCopper ?? 0, item.sellPrices.ninetiethPercentileCopper ?? 0]), 1);
  const x = (value: number) => margin.left + (value / maxValue) * (width - margin.left - margin.right);

  return (
    <div className="chart-card" data-testid="research-band-chart">
      <div className="chart-card-heading"><div><p className="eyebrow">Local observations</p><h3>Observed price bands</h3></div><p className="chart-caption">P10 · median · P90</p></div>
      <svg aria-labelledby="research-band-title research-band-description" className="chart-svg" role="img" viewBox={`0 0 ${width} ${height}`}>
        <title id="research-band-title">Observed local price bands</title>
        <desc id="research-band-description">Each row shows the observed tenth percentile, median, and ninetieth percentile for buy and sell prices.</desc>
        {eligibleItems.map((item, index) => {
          const y = margin.top + index * rowHeight + 20;
          return (
            <g key={item.itemId}>
              <text className="chart-point-label" x={0} y={y + 4}>#{item.itemId}</text>
              {renderBand(item.buyPrices, y - 12, 'research-buy', x)}
              {renderBand(item.sellPrices, y + 14, 'research-sell', x)}
              <text className="chart-tick-label" x={margin.left} y={height - 5}>0g</text>
              <text className="chart-tick-label" textAnchor="end" x={width - margin.right} y={height - 5}>{formatCopper(maxValue)}</text>
            </g>
          );
        })}
      </svg>
      <div className="chart-legend"><span><i className="legend-line legend-buy" /> Buy</span><span><i className="legend-line legend-sell" /> Sell</span></div>
      <p className="chart-note">Observed local evidence only. No forecast or investment advice is shown.</p>
      <details className="chart-data-details"><summary>View observed band data table</summary><table className="chart-data-table"><caption>Observed local price bands</caption><thead><tr><th scope="col">Item</th><th scope="col">Buy band</th><th scope="col">Sell band</th></tr></thead><tbody>{eligibleItems.map((item) => <tr key={item.itemId}><th scope="row">#{item.itemId}</th><td>{formatBand(item.buyPrices)}</td><td>{formatBand(item.sellPrices)}</td></tr>)}</tbody></table></details>
    </div>
  );
}

export function ResearchLiquidityChart({ items }: { items: MarketResearchWatchlistItem[] }) {
  const rows = items.filter((item) => item.buyLiquidity.coefficientOfVariationPercent !== null || item.sellLiquidity.coefficientOfVariationPercent !== null);
  if (rows.length === 0) {
    return <p className="chart-empty">Liquidity variability will appear after enough local observations are collected.</p>;
  }

  const width = 720;
  const height = Math.max(170, rows.length * 48 + 48);
  const margin = { top: 18, right: 32, bottom: 22, left: 150 };
  const maxValue = Math.max(...rows.flatMap((item) => [item.buyLiquidity.coefficientOfVariationPercent ?? 0, item.sellLiquidity.coefficientOfVariationPercent ?? 0]), 1);
  const x = (value: number) => margin.left + (value / maxValue) * (width - margin.left - margin.right);

  return (
    <div className="chart-card" data-testid="research-liquidity-chart">
      <div className="chart-card-heading"><div><p className="eyebrow">Liquidity context</p><h3>Observed liquidity variability</h3></div><p className="chart-caption">Coefficient of variation</p></div>
      <svg aria-labelledby="research-liquidity-title research-liquidity-description" className="chart-svg" role="img" viewBox={`0 0 ${width} ${height}`}>
        <title id="research-liquidity-title">Observed liquidity variability</title>
        <desc id="research-liquidity-description">Buy and sell coefficient of variation for each locally tracked item.</desc>
        {rows.map((item, index) => {
          const y = margin.top + index * 48 + 18;
          return <g key={item.itemId}><text className="chart-point-label" x={0} y={y + 4}>#{item.itemId}</text><line className="liquidity-buy-line" x1={margin.left} x2={x(item.buyLiquidity.coefficientOfVariationPercent ?? 0)} y1={y - 7} y2={y - 7} /><line className="liquidity-sell-line" x1={margin.left} x2={x(item.sellLiquidity.coefficientOfVariationPercent ?? 0)} y1={y + 12} y2={y + 12} /><circle className="liquidity-buy-point" cx={x(item.buyLiquidity.coefficientOfVariationPercent ?? 0)} cy={y - 7} r="5"><title>{`Buy variability: ${formatOptionalPercentage(item.buyLiquidity)}`}</title></circle><circle className="liquidity-sell-point" cx={x(item.sellLiquidity.coefficientOfVariationPercent ?? 0)} cy={y + 12} r="5"><title>{`Sell variability: ${formatOptionalPercentage(item.sellLiquidity)}`}</title></circle></g>;
        })}
        <text className="chart-tick-label" x={margin.left} y={height - 5}>0%</text><text className="chart-tick-label" textAnchor="end" x={width - margin.right} y={height - 5}>{formatPercentage(maxValue)}</text>
      </svg>
      <div className="chart-legend"><span><i className="legend-line legend-buy" /> Buy</span><span><i className="legend-line legend-sell" /> Sell</span></div>
      <p className="chart-note">Lower variability can be useful context, but does not guarantee execution.</p>
      <details className="chart-data-details"><summary>View liquidity data table</summary><table className="chart-data-table"><caption>Observed liquidity variability</caption><thead><tr><th scope="col">Item</th><th scope="col">Buy</th><th scope="col">Sell</th></tr></thead><tbody>{rows.map((item) => <tr key={item.itemId}><th scope="row">#{item.itemId}</th><td>{formatOptionalPercentage(item.buyLiquidity)}</td><td>{formatOptionalPercentage(item.sellLiquidity)}</td></tr>)}</tbody></table></details>
    </div>
  );
}

export function ResearchCoverageChart({ items }: { items: MarketResearchWatchlistItem[] }) {
  if (items.length === 0) {
    return <p className="chart-empty">Coverage will appear after items are added to the local watchlist.</p>;
  }

  const maxObservationCount = Math.max(...items.map((item) => item.coverage.observationCount), 1);

  return (
    <div className="chart-card coverage-chart" data-testid="research-coverage-chart">
      <div className="chart-card-heading"><div><p className="eyebrow">Evidence coverage</p><h3>Sample coverage</h3></div><p className="chart-caption">Count and observation window</p></div>
      <div className="coverage-strip" role="img" aria-label={items.map((item) => `${item.itemId}: ${item.coverage.observationCount} observations`).join(', ')}>
        {items.map((item) => (
          <div className="coverage-row" key={item.itemId}>
            <strong>#{item.itemId}</strong>
            <span className="coverage-track"><span style={{ width: `${(item.coverage.observationCount / maxObservationCount) * 100}%` }} /></span>
            <span className="coverage-count">{item.coverage.observationCount} samples</span>
          </div>
        ))}
      </div>
      <p className="chart-note">Observation windows are shown in the research table; small samples withhold summary metrics.</p>
      <details className="chart-data-details"><summary>View coverage data table</summary><table className="chart-data-table"><caption>Local research coverage</caption><thead><tr><th scope="col">Item</th><th scope="col">Samples</th><th scope="col">Observation window</th></tr></thead><tbody>{items.map((item) => <tr key={item.itemId}><th scope="row">#{item.itemId}</th><td>{item.coverage.observationCount}</td><td>{formatCoverageWindow(item.coverage.firstCapturedAtUtc, item.coverage.lastCapturedAtUtc)}</td></tr>)}</tbody></table></details>
    </div>
  );
}

function renderBand(
  statistics: MarketResearchPriceStatistics,
  y: number,
  className: string,
  x: (value: number) => number,
) {
  if (!hasPriceBand(statistics)) {
    return <text className="chart-tick-label" x={x(0)} y={y + 8}>Insufficient sample</text>;
  }

  return <g className={className}><line className="research-band-line" x1={x(statistics.tenthPercentileCopper!)} x2={x(statistics.ninetiethPercentileCopper!)} y1={y} y2={y} /><circle className="research-band-endpoint" cx={x(statistics.tenthPercentileCopper!)} cy={y} r="4" /><circle className="research-band-median" cx={x(statistics.medianCopper!)} cy={y} r="6"><title>{`Median ${formatCopper(statistics.medianCopper!)}`}</title></circle><circle className="research-band-endpoint" cx={x(statistics.ninetiethPercentileCopper!)} cy={y} r="4" /></g>;
}

function formatCoverageWindow(firstCapturedAtUtc: string | null, lastCapturedAtUtc: string | null): string {
  if (firstCapturedAtUtc === null || lastCapturedAtUtc === null) {
    return 'Unknown window';
  }

  return `${firstCapturedAtUtc} – ${lastCapturedAtUtc}`;
}

function hasPriceBand(statistics: MarketResearchPriceStatistics): boolean {
  return statistics.tenthPercentileCopper !== null && statistics.medianCopper !== null && statistics.ninetiethPercentileCopper !== null;
}

function formatBand(statistics: MarketResearchPriceStatistics): string {
  return hasPriceBand(statistics)
    ? `P10 ${formatCopper(statistics.tenthPercentileCopper!)} · median ${formatCopper(statistics.medianCopper!)} · P90 ${formatCopper(statistics.ninetiethPercentileCopper!)}`
    : `Insufficient sample (${statistics.observationCount} observed)`;
}

function formatOptionalPercentage(statistics: MarketResearchLiquidity): string {
  return statistics.coefficientOfVariationPercent === null ? `Insufficient sample (${statistics.observationCount} observed)` : formatPercentage(statistics.coefficientOfVariationPercent);
}

function formatCopper(copper: number): string {
  const absoluteCopper = Math.abs(copper);
  const gold = Math.floor(absoluteCopper / 10_000);
  const silver = Math.floor((absoluteCopper % 10_000) / 100);
  const remainingCopper = absoluteCopper % 100;
  return `${copper < 0 ? '−' : ''}${gold}g ${silver}s ${remainingCopper}c`;
}

function formatPercentage(value: number): string {
  return `${value.toFixed(2)}%`;
}
