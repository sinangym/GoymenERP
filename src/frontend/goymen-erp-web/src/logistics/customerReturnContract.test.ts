import { describe, expect, it } from "vitest";
import { decodeCustomerReturnResponseV1, decodeCustomerReturnViewV1 } from "./customerReturnContract";

const ids = {
  customerReturnId: "00000000-0000-4000-8000-000000000001",
  companyId: "00000000-0000-4000-8000-000000000002",
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
});
