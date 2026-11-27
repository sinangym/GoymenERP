import { describe, expect, it } from "vitest";
import { decodeCustomerReturnListV1, decodeCustomerReturnResponseV1, decodeCustomerReturnViewV1, isCustomerReturnWeighingRequest } from "./customerReturnContract";

const ids = {
  customerReturnId: "00000000-0000-4000-8000-000000000001",
  companyId: "10000000-0000-0000-0000-000000000001",
  loadingInstructionId: "00000000-0000-4000-8000-000000000003",
  loadingInstructionLineId: "00000000-0000-4000-8000-000000000004",
  sourceDeliveryId: "00000000-0000-4000-8000-000000000005",
  sourceShipmentId: "00000000-0000-4000-8000-000000000006",
  sourceOrderId: "00000000-0000-4000-8000-000000000007",
  sourceOrderLineId: "00000000-0000-4000-8000-000000000008",
  unitId: "00000000-0000-4000-8000-000000000009",
  inventoryMovementId: "00000000-0000-4000-8000-000000000010",
  originalLabelId: "00000000-0000-4000-8000-000000000011",
  originalShipmentMovementId: "00000000-0000-4000-8000-000000000012",
};

const correlationId = "00000000-0000-4000-8000-000000000013";

function pendingReturn() {
  return {
    ...ids,
    quantity: "2.500",
    sourceDeliveredQuantity: "4.000",
    status: "Pending",
    version: "1",
    reason: "Hasarlı ambalaj",
    requestedBy: "operator-a",
    occurredAtUtc: "2026-10-04T08:00:00Z",
    requestedAtUtc: "2026-10-04T08:01:00Z",
    decidedBy: null,
    decidedAtUtc: null,
    decisionNote: null,
    inventoryMovementId: null,
    originalLabelId: null,
    originalShipmentMovementId: null,
  };
}

describe("customer-return contract", () => {
  it("accepts an exact pending result in the expected company and instruction context", () => {
    const result = decodeCustomerReturnViewV1(pendingReturn(), {
      companyId: ids.companyId,
      loadingInstructionId: ids.loadingInstructionId,
      loadingInstructionLineId: ids.loadingInstructionLineId,
    });

    expect(result.ok).toBe(true);
  });

  it("accepts the canonical backend company GUID in list and replay envelopes", () => {
    const listed = decodeCustomerReturnListV1([pendingReturn()], { companyId: ids.companyId });
    const replay = decodeCustomerReturnResponseV1({
      return: pendingReturn(),
      isReplay: true,
      originalCorrelationId: correlationId,
    }, { companyId: ids.companyId });

    expect(listed.ok && listed.value[0]?.companyId).toBe(ids.companyId);
    expect(replay.ok && replay.value.return.companyId).toBe(ids.companyId);
  });

  it.each([
    "00000000-0000-0000-0000-000000000000",
    "10000000-0000-0000-0000-00000000000g",
    "10000000-0000-0000-0000-00000000001",
  ])("rejects an empty or malformed company GUID: %s", (companyId) => {
    expect(decodeCustomerReturnViewV1({ ...pendingReturn(), companyId })).toEqual({
      ok: false,
      error: "customer-return-member",
    });
  });

  it("rejects unknown members and cross-company data", () => {
    expect(decodeCustomerReturnViewV1({ ...pendingReturn(), unexpected: true }).ok).toBe(false);
    expect(decodeCustomerReturnViewV1(pendingReturn(), {
      companyId: "00000000-0000-4000-8000-000000000099",
    })).toEqual({ ok: false, error: "customer-return-context" });
  });

  it("rejects impossible terminal state shapes", () => {
    const impossible = { ...pendingReturn(), status: "Approved", version: "2" };
    expect(decodeCustomerReturnViewV1(impossible)).toEqual({ ok: false, error: "customer-return-state" });
  });

  it("decodes the immutable response envelope without changing replay identity", () => {
    const result = decodeCustomerReturnResponseV1({
      return: pendingReturn(),
      isReplay: true,
      originalCorrelationId: correlationId,
    });

    expect(result.ok && result.value.isReplay).toBe(true);
    expect(result.ok && result.value.return.status).toBe("Pending");
  });

  it.each([
    "1234567890abcdef1234567890abcdef",
    "opaque-original-correlation/attempt:2026-10-04",
  ])("accepts and preserves a bounded non-UUID correlation identity: %s", (originalCorrelationId) => {
    const result = decodeCustomerReturnResponseV1({
      return: pendingReturn(),
      isReplay: true,
      originalCorrelationId,
    });

    expect(result.ok && result.value.originalCorrelationId).toBe(originalCorrelationId);
  });

  it.each(["", "   ", "x".repeat(129), 42])("rejects a malformed correlation identity: %s", (originalCorrelationId) => {
    expect(decodeCustomerReturnResponseV1({
      return: pendingReturn(),
      isReplay: true,
      originalCorrelationId,
    }).ok).toBe(false);
  });
});

const measuredWeighing = { weighingId: ids.inventoryMovementId, direction: 'GrossFirst', firstWeighingKg: '1013.125', secondWeighingKg: '1000',
  netKg: '13.125', recordedBy: 'SENTETIK operator', recordedAtUtc: '2026-11-27T08:00:00Z' };
it('keeps old unweighed snapshots and the measured Pending2/terminal3 source distinct without deriving kg', () => {
  expect(decodeCustomerReturnViewV1(pendingReturn()).ok).toBe(true);
  const measured = { ...pendingReturn(), weighing: measuredWeighing, version: '2' };
  expect(decodeCustomerReturnViewV1(measured, { quantityUnit: 'Piece' }).ok).toBe(true);
  const terminal = { ...measured, status: 'Rejected', version: '3', decidedBy: 'checker', decidedAtUtc: '2026-11-27T09:00:00Z' };
  expect(decodeCustomerReturnViewV1(terminal, { quantityUnit: 'Piece' }).ok).toBe(true);
  expect(decodeCustomerReturnViewV1(measured, { quantityUnit: 'Kilogram' }).ok).toBe(false);
  expect(decodeCustomerReturnViewV1({ ...measured, version: '1' }).ok).toBe(false);
  expect(decodeCustomerReturnViewV1({ ...terminal, version: '2' }).ok).toBe(false);
  expect(decodeCustomerReturnViewV1({ ...pendingReturn(), status: 'Approved', version: '2', decidedBy: 'checker',
    decidedAtUtc: '2026-11-27T09:00:00Z', inventoryMovementId: ids.inventoryMovementId, originalLabelId: ids.originalLabelId,
    originalShipmentMovementId: ids.originalShipmentMovementId }, { quantityUnit: 'Piece' }).ok).toBe(false);
});
it.each([null, { ...measuredWeighing, direction: ['GrossFirst'] }, { ...measuredWeighing, netKg: '13.125\n' },
  { ...measuredWeighing, weighingId: ids.inventoryMovementId + '\n' }, { ...measuredWeighing, recordedBy: '' },
  { ...measuredWeighing, recordedAtUtc: '2026-02-30T08:00:00Z' }, { ...measuredWeighing, recordedAtUtc: '2026-11-27T08:00:00+03:00' },
  { ...measuredWeighing, extra: 'private' }])('refuses malformed retained weighing evidence %j', weighing => {
  expect(decodeCustomerReturnViewV1({ ...pendingReturn(), version: '2', weighing }).ok).toBe(false);
});
it('accepts only the five native command fields and never a client net or current version replacement', () => {
  const request = { expectedVersion: '1', weighingId: ids.inventoryMovementId, direction: 'GrossFirst', firstWeighingKg: '1013.125', secondWeighingKg: '1000' };
  expect(isCustomerReturnWeighingRequest(request)).toBe(true);
  for (const delta of [{ expectedVersion: '2' }, { netKg: '13.125' }, { firstWeighingKg: '1013.125\n' }, { direction: ['GrossFirst'] }])
    expect(isCustomerReturnWeighingRequest({ ...request, ...delta })).toBe(false);
});
