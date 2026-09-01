import type { RiskProfile } from './m9';

const MAXIMUM_SAFE_INTEGER = Number.MAX_SAFE_INTEGER;
const MAXIMUM_CANDIDATE_COUNT = 200;
const EXPECTED_CONTRACT_VERSION = 1;
const EXPECTED_NORMAL_STACK_LIMIT = 250;
const EXPECTED_CAPTURE_POLICY = {
  requestsPerSecond: 2,
  maxConcurrentRequests: 2,
  burstBudget: 20,
} as const;
const BASIS_POINTS_PER_WHOLE = 10_000n;
const INT32_MAXIMUM = 2_147_483_647n;

export type MarketSnapshotParseErrorKind = 'incompatible-snapshot' | 'malformed-snapshot';

/**
 * A parse failure that lets a future static UI distinguish unsupported data
 * from malformed or incomplete data without attempting a recommendation.
 */
export class MarketSnapshotParseError extends Error {
  constructor(
    public readonly kind: MarketSnapshotParseErrorKind,
    message: string,
  ) {
    super(message);
    this.name = 'MarketSnapshotParseError';
  }
}

export interface MarketSnapshot {
  contractVersion: number;
  generatedAtUtc: string;
  compatibility: MarketSnapshotCompatibility;
  capturePolicy: MarketSnapshotCapturePolicy;
  candidates: MarketSnapshotCandidate[];
}

export interface MarketSnapshotCompatibility {
  moneyUnit: 'copper';
  recommendationPolicyVersion: 'm9-v1';
  normalStackLimit: bigint;
}

export interface MarketSnapshotCapturePolicy {
  requestsPerSecond: number;
  maxConcurrentRequests: number;
  burstBudget: number;
}

export interface MarketSnapshotCandidate {
  itemId: number;
  itemName: string;
  buys: MarketSnapshotOrderLevel[];
  sells: MarketSnapshotOrderLevel[];
}

export interface MarketSnapshotOrderLevel {
  listingCount: bigint;
  quantity: bigint;
  unitPriceInCopper: bigint;
}

export type SnapshotRecommendationRoute = 'can-act-now' | 'place-order-and-wait';

export interface SnapshotRecommendationInput {
  capitalCopper: bigint;
  riskProfile: RiskProfile;
}

export interface SnapshotExactRoi {
  profitCopper: bigint;
  totalCostCopper: bigint;
}

export interface SnapshotRouteEvidence {
  sellerQuantityAtOrBelowBuyPrice: bigint;
  coversSelectedQuantity: boolean;
}

export interface SnapshotRecommendation {
  rank: number;
  itemId: number;
  itemName: string;
  route: SnapshotRecommendationRoute;
  quantity: bigint;
  buyUnitPriceCopper: bigint;
  saleUnitPriceCopper: bigint;
  buyOrderReserveCopper: bigint;
  grossSaleCopper: bigint;
  listingFeeCopper: bigint;
  exchangeFeeCopper: bigint;
  netSaleProceedsCopper: bigint;
  totalCostCopper: bigint;
  modeledProfitCopper: bigint;
  modeledRoi: SnapshotExactRoi;
  snapshotGeneratedAtUtc: string;
  routeEvidence: SnapshotRouteEvidence;
  assumptions: string[];
}

export interface SnapshotRecommendationResult {
  capitalCopper: bigint;
  riskProfile: RiskProfile;
  spendCapCopper: bigint;
  snapshotGeneratedAtUtc: string;
  recommendations: SnapshotRecommendation[];
  canActNow: SnapshotRecommendation[];
  placeOrderAndWait: SnapshotRecommendation[];
}

interface RiskProfilePolicy {
  spendCapBasisPoints: bigint;
  minimumRoiBasisPoints: bigint;
  minimumProfitCopper: bigint;
}

interface RecommendationMetrics {
  quantity: bigint;
  buyOrderReserveCopper: bigint;
  grossSaleCopper: bigint;
  listingFeeCopper: bigint;
  exchangeFeeCopper: bigint;
  netSaleProceedsCopper: bigint;
  totalCostCopper: bigint;
  modeledProfitCopper: bigint;
}

const riskProfilePolicies: Record<RiskProfile, RiskProfilePolicy> = {
  cautious: {
    spendCapBasisPoints: 1_000n,
    minimumRoiBasisPoints: 500n,
    minimumProfitCopper: 1_000n,
  },
  balanced: {
    spendCapBasisPoints: 2_500n,
    minimumRoiBasisPoints: 800n,
    minimumProfitCopper: 2_500n,
  },
  adventurous: {
    spendCapBasisPoints: 5_000n,
    minimumRoiBasisPoints: 1_200n,
    minimumProfitCopper: 5_000n,
  },
};

const recommendationAssumptions = [
  'current-order-book-snapshot-only',
  'current-order-book-depth-and-spread-guard',
  'manual-in-game-orders-required',
  'no-execution-sale-or-profit-guarantee',
  'fee-rounding-pending-external-verification',
];

/**
 * Parses the complete, versioned M10 market snapshot contract. No untrusted
 * JSON number is converted to BigInt until its safe-integer representation has
 * been validated.
 */
export function parseMarketSnapshot(value: unknown): MarketSnapshot {
  const root = requiredRecord(value, 'The market snapshot must be a JSON object.');
  const contractVersion = requiredSafeInteger(root, 'contractVersion');
  if (contractVersion !== EXPECTED_CONTRACT_VERSION) {
    throw incompatible(`Market snapshot contract version ${contractVersion} is not supported.`);
  }

  const generatedAtUtc = requiredCanonicalUtcTimestamp(root, 'generatedAtUtc');
  const compatibility = parseCompatibility(requiredRecord(root.compatibility, 'The compatibility metadata is required.'));
  const capturePolicy = parseCapturePolicy(requiredRecord(root.capturePolicy, 'The capture policy metadata is required.'));
  const candidatesValue = requiredArray(root, 'candidates');
  if (candidatesValue.length > MAXIMUM_CANDIDATE_COUNT) {
    throw malformed(`The market snapshot cannot contain more than ${MAXIMUM_CANDIDATE_COUNT} candidates.`);
  }

  let previousItemId = 0;
  const candidates = candidatesValue.map((candidateValue, index) => {
    const candidate = requiredRecord(candidateValue, `Candidate ${index + 1} must be an object.`);
    const itemId = requiredPositiveSafeInteger(candidate, 'itemId');
    if (itemId <= previousItemId) {
      throw malformed('Snapshot candidates must have distinct, ascending item IDs.');
    }

    const itemName = requiredNonBlankString(candidate, 'itemName');
    const buys = parseOrderLevels(requiredArray(candidate, 'buys'), `candidate ${itemId} buys`);
    const sells = parseOrderLevels(requiredArray(candidate, 'sells'), `candidate ${itemId} sells`);
    if (buys.length === 0 || sells.length === 0) {
      throw malformed('Snapshot candidates must include complete buy and sell order-book data.');
    }

    previousItemId = itemId;
    return { itemId, itemName, buys, sells };
  });

  return {
    contractVersion,
    generatedAtUtc,
    compatibility,
    capturePolicy,
    candidates,
  };
}

/**
 * Recalculates the fixed M9 beginner policy entirely in BigInt from one
 * validated static market snapshot. It never reads the network or storage.
 */
export function calculateSnapshotRecommendations(
  snapshot: MarketSnapshot,
  input: SnapshotRecommendationInput,
): SnapshotRecommendationResult {
  if (input.capitalCopper < 0n) {
    throw new RangeError('Capital cannot be negative.');
  }

  const policy = riskProfilePolicies[input.riskProfile];
  const spendCapCopper = (input.capitalCopper * policy.spendCapBasisPoints) / BASIS_POINTS_PER_WHOLE;
  const recommendations = snapshot.candidates
    .map((candidate) => createRecommendation(candidate, snapshot, spendCapCopper, policy))
    .filter((recommendation): recommendation is SnapshotRecommendation => recommendation !== null)
    .sort(compareRecommendations)
    .slice(0, 5)
    .map((recommendation, index) => ({ ...recommendation, rank: index + 1 }));

  return {
    capitalCopper: input.capitalCopper,
    riskProfile: input.riskProfile,
    spendCapCopper,
    snapshotGeneratedAtUtc: snapshot.generatedAtUtc,
    recommendations,
    canActNow: recommendations.filter((recommendation) => recommendation.route === 'can-act-now'),
    placeOrderAndWait: recommendations.filter((recommendation) => recommendation.route === 'place-order-and-wait'),
  };
}

/** Returns the existing owner-selected M9 fee policy for a positive gross sale. */
export function calculateSnapshotFees(grossSaleCopper: bigint): {
  listingFeeCopper: bigint;
  exchangeFeeCopper: bigint;
} {
  if (grossSaleCopper < 0n) {
    throw new RangeError('A gross sale value cannot be negative.');
  }

  return {
    listingFeeCopper: calculateCeilingFee(grossSaleCopper, 500n),
    exchangeFeeCopper: calculateCeilingFee(grossSaleCopper, 1_000n),
  };
}

function parseCompatibility(value: Record<string, unknown>): MarketSnapshotCompatibility {
  if (requiredExactString(value, 'moneyUnit', 'copper') === null ||
    requiredExactString(value, 'recommendationPolicyVersion', 'm9-v1') === null ||
    requiredSafeInteger(value, 'normalStackLimit') !== EXPECTED_NORMAL_STACK_LIMIT) {
    throw incompatible('The market snapshot compatibility metadata is not supported.');
  }

  return {
    moneyUnit: 'copper',
    recommendationPolicyVersion: 'm9-v1',
    normalStackLimit: BigInt(EXPECTED_NORMAL_STACK_LIMIT),
  };
}

function parseCapturePolicy(value: Record<string, unknown>): MarketSnapshotCapturePolicy {
  const requestsPerSecond = requiredSafeInteger(value, 'requestsPerSecond');
  const maxConcurrentRequests = requiredSafeInteger(value, 'maxConcurrentRequests');
  const burstBudget = requiredSafeInteger(value, 'burstBudget');
  if (requestsPerSecond !== EXPECTED_CAPTURE_POLICY.requestsPerSecond ||
    maxConcurrentRequests !== EXPECTED_CAPTURE_POLICY.maxConcurrentRequests ||
    burstBudget !== EXPECTED_CAPTURE_POLICY.burstBudget) {
    throw incompatible('The market snapshot capture policy metadata is not supported.');
  }

  return { requestsPerSecond, maxConcurrentRequests, burstBudget };
}

function parseOrderLevels(value: unknown[], description: string): MarketSnapshotOrderLevel[] {
  let previous: MarketSnapshotOrderLevel | null = null;
  return value.map((levelValue, index) => {
    const level = requiredRecord(levelValue, `${description} level ${index + 1} must be an object.`);
    const parsed = {
      listingCount: BigInt(requiredPositiveSafeInteger(level, 'listingCount')),
      quantity: BigInt(requiredPositiveSafeInteger(level, 'quantity')),
      unitPriceInCopper: BigInt(requiredPositiveSafeInteger(level, 'unitPriceInCopper')),
    };
    if (previous !== null && compareOrderLevels(previous, parsed) > 0) {
      throw malformed(`${description} levels must use canonical ordering.`);
    }

    previous = parsed;
    return parsed;
  });
}

function createRecommendation(
  candidate: MarketSnapshotCandidate,
  snapshot: MarketSnapshot,
  spendCapCopper: bigint,
  policy: RiskProfilePolicy,
): SnapshotRecommendation | null {
  if (candidate.buys.length === 0 || candidate.sells.length === 0 ||
    !hasMinimumDetailedOrderBookDepth(candidate.buys) || !hasMinimumDetailedOrderBookDepth(candidate.sells)) {
    return null;
  }

  const bestBuyerPrice = candidate.buys.reduce(
    (maximum, level) => level.unitPriceInCopper > maximum ? level.unitPriceInCopper : maximum,
    0n,
  );
  const cheapestSellerPrice = candidate.sells.reduce(
    (minimum, level) => level.unitPriceInCopper < minimum ? level.unitPriceInCopper : minimum,
    candidate.sells[0].unitPriceInCopper,
  );
  if (bestBuyerPrice >= INT32_MAXIMUM || cheapestSellerPrice <= 1n) {
    return null;
  }

  const buyUnitPriceCopper = bestBuyerPrice + 1n;
  const saleUnitPriceCopper = cheapestSellerPrice - 1n;
  const maximumQuantity = minimum(
    snapshot.compatibility.normalStackLimit,
    spendCapCopper / buyUnitPriceCopper,
  );
  if (maximumQuantity === 0n) {
    return null;
  }

  const metrics = findLargestAffordableMetrics(maximumQuantity, spendCapCopper, buyUnitPriceCopper, saleUnitPriceCopper);
  if (metrics === null || metrics.modeledProfitCopper < policy.minimumProfitCopper ||
    !meetsRoiThreshold(metrics.modeledProfitCopper, metrics.totalCostCopper, policy.minimumRoiBasisPoints)) {
    return null;
  }

  const sellerQuantityAtOrBelowBuyPrice = candidate.sells.reduce(
    (total, level) => level.unitPriceInCopper <= buyUnitPriceCopper ? total + level.quantity : total,
    0n,
  );
  const coversSelectedQuantity = sellerQuantityAtOrBelowBuyPrice >= metrics.quantity;
  return {
    rank: 0,
    itemId: candidate.itemId,
    itemName: candidate.itemName,
    route: coversSelectedQuantity ? 'can-act-now' : 'place-order-and-wait',
    quantity: metrics.quantity,
    buyUnitPriceCopper,
    saleUnitPriceCopper,
    buyOrderReserveCopper: metrics.buyOrderReserveCopper,
    grossSaleCopper: metrics.grossSaleCopper,
    listingFeeCopper: metrics.listingFeeCopper,
    exchangeFeeCopper: metrics.exchangeFeeCopper,
    netSaleProceedsCopper: metrics.netSaleProceedsCopper,
    totalCostCopper: metrics.totalCostCopper,
    modeledProfitCopper: metrics.modeledProfitCopper,
    modeledRoi: {
      profitCopper: metrics.modeledProfitCopper,
      totalCostCopper: metrics.totalCostCopper,
    },
    snapshotGeneratedAtUtc: snapshot.generatedAtUtc,
    routeEvidence: { sellerQuantityAtOrBelowBuyPrice, coversSelectedQuantity },
    assumptions: [...recommendationAssumptions],
  };
}

function findLargestAffordableMetrics(
  maximumQuantity: bigint,
  spendCapCopper: bigint,
  buyUnitPriceCopper: bigint,
  saleUnitPriceCopper: bigint,
): RecommendationMetrics | null {
  let lowerBound = 0n;
  let upperBound = maximumQuantity;
  while (lowerBound < upperBound) {
    const candidateQuantity = lowerBound + ((upperBound - lowerBound + 1n) / 2n);
    const metrics = calculateMetrics(candidateQuantity, buyUnitPriceCopper, saleUnitPriceCopper);
    if (metrics.totalCostCopper <= spendCapCopper) {
      lowerBound = candidateQuantity;
    } else {
      upperBound = candidateQuantity - 1n;
    }
  }

  return lowerBound === 0n
    ? null
    : calculateMetrics(lowerBound, buyUnitPriceCopper, saleUnitPriceCopper);
}

function calculateMetrics(
  quantity: bigint,
  buyUnitPriceCopper: bigint,
  saleUnitPriceCopper: bigint,
): RecommendationMetrics {
  const buyOrderReserveCopper = buyUnitPriceCopper * quantity;
  const grossSaleCopper = saleUnitPriceCopper * quantity;
  const { listingFeeCopper, exchangeFeeCopper } = calculateSnapshotFees(grossSaleCopper);
  const netSaleProceedsCopper = grossSaleCopper - listingFeeCopper - exchangeFeeCopper;
  const totalCostCopper = buyOrderReserveCopper + listingFeeCopper;
  return {
    quantity,
    buyOrderReserveCopper,
    grossSaleCopper,
    listingFeeCopper,
    exchangeFeeCopper,
    netSaleProceedsCopper,
    totalCostCopper,
    modeledProfitCopper: netSaleProceedsCopper - buyOrderReserveCopper,
  };
}

function calculateCeilingFee(grossSaleCopper: bigint, basisPoints: bigint): bigint {
  const calculatedFee = (grossSaleCopper * basisPoints + BASIS_POINTS_PER_WHOLE - 1n) / BASIS_POINTS_PER_WHOLE;
  return grossSaleCopper > 0n && calculatedFee < 1n ? 1n : calculatedFee;
}

function hasMinimumDetailedOrderBookDepth(levels: MarketSnapshotOrderLevel[]): boolean {
  const totals = levels.reduce(
    (current, level) => ({
      listingCount: current.listingCount + level.listingCount,
      quantity: current.quantity + level.quantity,
    }),
    { listingCount: 0n, quantity: 0n },
  );
  return totals.listingCount >= 3n && totals.quantity >= 10n;
}

function meetsRoiThreshold(profitCopper: bigint, totalCostCopper: bigint, basisPoints: bigint): boolean {
  return totalCostCopper > 0n && profitCopper * BASIS_POINTS_PER_WHOLE >= totalCostCopper * basisPoints;
}

function compareRecommendations(left: SnapshotRecommendation, right: SnapshotRecommendation): number {
  const profitComparison = compareBigIntDescending(left.modeledProfitCopper, right.modeledProfitCopper);
  if (profitComparison !== 0) return profitComparison;

  const roiComparison = compareBigIntDescending(
    left.modeledProfitCopper * right.totalCostCopper,
    right.modeledProfitCopper * left.totalCostCopper,
  );
  if (roiComparison !== 0) return roiComparison;

  const costComparison = compareBigIntAscending(left.totalCostCopper, right.totalCostCopper);
  return costComparison !== 0 ? costComparison : left.itemId - right.itemId;
}

function compareOrderLevels(left: MarketSnapshotOrderLevel, right: MarketSnapshotOrderLevel): number {
  const priceComparison = compareBigIntAscending(left.unitPriceInCopper, right.unitPriceInCopper);
  if (priceComparison !== 0) return priceComparison;

  const quantityComparison = compareBigIntAscending(left.quantity, right.quantity);
  return quantityComparison !== 0
    ? quantityComparison
    : compareBigIntAscending(left.listingCount, right.listingCount);
}

function compareBigIntAscending(left: bigint, right: bigint): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

function compareBigIntDescending(left: bigint, right: bigint): number {
  return left > right ? -1 : left < right ? 1 : 0;
}

function minimum(left: bigint, right: bigint): bigint {
  return left < right ? left : right;
}

function requiredRecord(value: unknown, message: string): Record<string, unknown> {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) throw malformed(message);
  return value as Record<string, unknown>;
}

function requiredArray(value: Record<string, unknown>, field: string): unknown[] {
  const candidate = value[field];
  if (!Array.isArray(candidate)) throw malformed(`The ${field} array is required.`);
  return candidate;
}

function requiredSafeInteger(value: Record<string, unknown>, field: string): number {
  const candidate = value[field];
  if (typeof candidate !== 'number' || !Number.isSafeInteger(candidate)) {
    throw malformed(`${field} must be a safe JSON integer.`);
  }

  return candidate;
}

function requiredPositiveSafeInteger(value: Record<string, unknown>, field: string): number {
  const candidate = requiredSafeInteger(value, field);
  if (candidate <= 0 || candidate > MAXIMUM_SAFE_INTEGER) throw malformed(`${field} must be positive.`);
  return candidate;
}

function requiredNonBlankString(value: Record<string, unknown>, field: string): string {
  const candidate = value[field];
  if (typeof candidate !== 'string' || candidate.trim().length === 0) {
    throw malformed(`${field} must be a non-blank string.`);
  }

  return candidate;
}

function requiredExactString(value: Record<string, unknown>, field: string, expected: string): string | null {
  const candidate = value[field];
  if (typeof candidate !== 'string') throw malformed(`${field} must be a string.`);
  return candidate === expected ? candidate : null;
}

function requiredCanonicalUtcTimestamp(value: Record<string, unknown>, field: string): string {
  const candidate = value[field];
  if (typeof candidate !== 'string') throw malformed(`${field} must be a string.`);

  const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})\.(\d{7})Z$/u.exec(candidate);
  if (match === null || candidate.startsWith('0000')) {
    throw malformed(`${field} must be a canonical UTC ISO-8601 timestamp.`);
  }

  const parsed = new Date(candidate);
  if (Number.isNaN(parsed.getTime()) || parsed.toISOString() !== `${match[1]}.${match[2].slice(0, 3)}Z`) {
    throw malformed(`${field} must be a canonical UTC ISO-8601 timestamp.`);
  }

  return candidate;
}

function malformed(message: string): MarketSnapshotParseError {
  return new MarketSnapshotParseError('malformed-snapshot', message);
}

function incompatible(message: string): MarketSnapshotParseError {
  return new MarketSnapshotParseError('incompatible-snapshot', message);
}
