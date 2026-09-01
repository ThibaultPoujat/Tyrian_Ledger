import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it, vi } from 'vitest';
import {
  calculateSnapshotFees,
  calculateSnapshotRecommendations,
  MarketSnapshotParseError,
  parseMarketSnapshot,
  type SnapshotRecommendation,
  type SnapshotRecommendationResult,
} from './marketSnapshot';

interface GoldenVectorDocument {
  formatVersion: number;
  feeVectors: GoldenFeeVector[];
  recommendationVectors: GoldenRecommendationVector[];
}

interface GoldenFeeVector {
  name: string;
  grossSaleCopper: string;
  listingFeeCopper: string;
  exchangeFeeCopper: string;
}

interface GoldenRecommendationVector {
  name: string;
  capitalCopper: string;
  riskProfile: 'cautious' | 'balanced' | 'adventurous';
  snapshot: unknown;
  expected: GoldenRecommendationExpected;
}

interface GoldenRecommendationExpected {
  spendCapCopper: string;
  recommendations: GoldenRecommendation[];
}

interface GoldenRecommendation {
  rank: number;
  itemId: number;
  itemName: string;
  route: 'can-act-now' | 'place-order-and-wait';
  quantity: string;
  buyUnitPriceCopper: string;
  saleUnitPriceCopper: string;
  buyOrderReserveCopper: string;
  grossSaleCopper: string;
  listingFeeCopper: string;
  exchangeFeeCopper: string;
  netSaleProceedsCopper: string;
  totalCostCopper: string;
  modeledProfitCopper: string;
  sellerQuantityAtOrBelowBuyPrice: string;
  coversSelectedQuantity: boolean;
}

const goldenFixturePath = resolve(
  process.cwd(),
  '../tests/fixtures/recommendations/browser-recommendation-golden-v1.json',
);
const goldenVectors = JSON.parse(readFileSync(goldenFixturePath, 'utf8')) as GoldenVectorDocument;
const validSnapshot = goldenVectors.recommendationVectors[0].snapshot;

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}

function expectParseFailure(value: unknown, kind: MarketSnapshotParseError['kind']): void {
  try {
    parseMarketSnapshot(value);
    throw new Error('Expected the snapshot parser to reject the value.');
  } catch (error) {
    expect(error).toBeInstanceOf(MarketSnapshotParseError);
    expect((error as MarketSnapshotParseError).kind).toBe(kind);
  }
}

function toGoldenRecommendation(recommendation: SnapshotRecommendation): GoldenRecommendation {
  return {
    rank: recommendation.rank,
    itemId: recommendation.itemId,
    itemName: recommendation.itemName,
    route: recommendation.route,
    quantity: recommendation.quantity.toString(),
    buyUnitPriceCopper: recommendation.buyUnitPriceCopper.toString(),
    saleUnitPriceCopper: recommendation.saleUnitPriceCopper.toString(),
    buyOrderReserveCopper: recommendation.buyOrderReserveCopper.toString(),
    grossSaleCopper: recommendation.grossSaleCopper.toString(),
    listingFeeCopper: recommendation.listingFeeCopper.toString(),
    exchangeFeeCopper: recommendation.exchangeFeeCopper.toString(),
    netSaleProceedsCopper: recommendation.netSaleProceedsCopper.toString(),
    totalCostCopper: recommendation.totalCostCopper.toString(),
    modeledProfitCopper: recommendation.modeledProfitCopper.toString(),
    sellerQuantityAtOrBelowBuyPrice: recommendation.routeEvidence.sellerQuantityAtOrBelowBuyPrice.toString(),
    coversSelectedQuantity: recommendation.routeEvidence.coversSelectedQuantity,
  };
}

describe('market snapshot parser', () => {
  it('parses the complete v1 snapshot contract into BigInt calculation inputs', () => {
    const parsed = parseMarketSnapshot(validSnapshot);

    expect(parsed.contractVersion).toBe(1);
    expect(parsed.generatedAtUtc).toBe('2026-09-01T12:00:00.0000000Z');
    expect(parsed.compatibility.normalStackLimit).toBe(250n);
    expect(parsed.candidates[0].buys[0]).toEqual({
      listingCount: 3n,
      quantity: 100n,
      unitPriceInCopper: 999n,
    });
  });

  it('rejects incompatible versions and metadata before calculation', () => {
    const unknownVersion = clone(validSnapshot) as Record<string, unknown>;
    unknownVersion.contractVersion = 2;
    expectParseFailure(unknownVersion, 'incompatible-snapshot');

    const incompatibleMetadata = clone(validSnapshot) as Record<string, unknown>;
    (incompatibleMetadata.compatibility as Record<string, unknown>).moneyUnit = 'gold';
    expectParseFailure(incompatibleMetadata, 'incompatible-snapshot');

    const incompatibleCapturePolicy = clone(validSnapshot) as Record<string, unknown>;
    (incompatibleCapturePolicy.capturePolicy as Record<string, unknown>).burstBudget = 21;
    expectParseFailure(incompatibleCapturePolicy, 'incompatible-snapshot');
  });

  it('rejects malformed, incomplete, non-canonical, and unsafe numeric input', () => {
    const missingCandidates = clone(validSnapshot) as Record<string, unknown>;
    delete missingCandidates.candidates;
    expectParseFailure(missingCandidates, 'malformed-snapshot');

    const incompleteCandidate = clone(validSnapshot) as Record<string, unknown>;
    (incompleteCandidate.candidates as Array<Record<string, unknown>>)[0].sells = [];
    expectParseFailure(incompleteCandidate, 'malformed-snapshot');

    const malformedTimestamp = clone(validSnapshot) as Record<string, unknown>;
    malformedTimestamp.generatedAtUtc = '2026-09-01T12:00:00Z';
    expectParseFailure(malformedTimestamp, 'malformed-snapshot');

    const invalidDate = clone(validSnapshot) as Record<string, unknown>;
    invalidDate.generatedAtUtc = '2026-02-30T12:00:00.0000000Z';
    expectParseFailure(invalidDate, 'malformed-snapshot');

    const maximumSafeCopper = clone(validSnapshot) as Record<string, unknown>;
    const maximumSafeCandidate = (maximumSafeCopper.candidates as Array<Record<string, unknown>>)[0];
    ((maximumSafeCandidate.buys as Array<Record<string, unknown>>)[0]).unitPriceInCopper = Number.MAX_SAFE_INTEGER;
    expect(parseMarketSnapshot(maximumSafeCopper).candidates[0].buys[0].unitPriceInCopper).toBe(
      BigInt(Number.MAX_SAFE_INTEGER),
    );

    const unsafeCopper = clone(validSnapshot) as Record<string, unknown>;
    const unsafeCandidate = (unsafeCopper.candidates as Array<Record<string, unknown>>)[0];
    ((unsafeCandidate.buys as Array<Record<string, unknown>>)[0]).unitPriceInCopper = Number.MAX_SAFE_INTEGER + 1;
    expectParseFailure(unsafeCopper, 'malformed-snapshot');

    const fractionalQuantity = clone(validSnapshot) as Record<string, unknown>;
    const fractionalCandidate = (fractionalQuantity.candidates as Array<Record<string, unknown>>)[0];
    ((fractionalCandidate.sells as Array<Record<string, unknown>>)[0]).quantity = 2.5;
    expectParseFailure(fractionalQuantity, 'malformed-snapshot');
  });

  it('rejects duplicate or unordered candidates and order levels', () => {
    const unorderedCandidates = clone(validSnapshot) as Record<string, unknown>;
    const secondCandidate = clone((unorderedCandidates.candidates as Array<Record<string, unknown>>)[0]);
    secondCandidate.itemId = 1;
    (unorderedCandidates.candidates as Array<Record<string, unknown>>).push(secondCandidate);
    expectParseFailure(unorderedCandidates, 'malformed-snapshot');

    const unorderedLevels = clone(validSnapshot) as Record<string, unknown>;
    const candidate = (unorderedLevels.candidates as Array<Record<string, unknown>>)[0];
    (candidate.buys as Array<Record<string, unknown>>).push({
      listingCount: 3,
      quantity: 100,
      unitPriceInCopper: 998,
    });
    expectParseFailure(unorderedLevels, 'malformed-snapshot');
  });
});

describe('BigInt snapshot recommendations', () => {
  it('matches every C# golden vector without browser network traffic', () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    try {
      expect(goldenVectors.formatVersion).toBe(1);
      for (const vector of goldenVectors.feeVectors) {
        const fees = calculateSnapshotFees(BigInt(vector.grossSaleCopper));
        expect(fees.listingFeeCopper.toString(), vector.name).toBe(vector.listingFeeCopper);
        expect(fees.exchangeFeeCopper.toString(), vector.name).toBe(vector.exchangeFeeCopper);
      }

      for (const vector of goldenVectors.recommendationVectors) {
        const snapshot = parseMarketSnapshot(vector.snapshot);
        const result = calculateSnapshotRecommendations(snapshot, {
          capitalCopper: BigInt(vector.capitalCopper),
          riskProfile: vector.riskProfile,
        });
        expectGoldenResult(result, vector.expected, vector.name, snapshot.generatedAtUtc);
      }

      expect(fetchMock).not.toHaveBeenCalled();
    } finally {
      vi.unstubAllGlobals();
    }
  });
});

function expectGoldenResult(
  actual: SnapshotRecommendationResult,
  expected: GoldenRecommendationExpected,
  name: string,
  generatedAtUtc: string,
): void {
  expect(actual.spendCapCopper.toString(), name).toBe(expected.spendCapCopper);
  expect(actual.snapshotGeneratedAtUtc, name).toBe(generatedAtUtc);
  expect(actual.recommendations.map(toGoldenRecommendation), name).toEqual(expected.recommendations);
  expect(actual.canActNow, name).toEqual([]);
  expect(actual.placeOrderAndWait.map(toGoldenRecommendation), name).toEqual(expected.recommendations);

  for (const recommendation of actual.recommendations) {
    expect(recommendation.snapshotGeneratedAtUtc, name).toBe(generatedAtUtc);
    expect(recommendation.modeledRoi, name).toEqual({
      profitCopper: recommendation.modeledProfitCopper,
      totalCostCopper: recommendation.totalCostCopper,
    });
    expect(recommendation.assumptions, name).toEqual([
      'current-order-book-snapshot-only',
      'current-order-book-depth-and-spread-guard',
      'manual-in-game-orders-required',
      'no-execution-sale-or-profit-guarantee',
      'fee-rounding-pending-external-verification',
    ]);
  }
}
