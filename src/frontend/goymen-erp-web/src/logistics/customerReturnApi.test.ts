import { describe, expect, it } from "vitest";
import {
  canRetryCustomerReturnAttempt,
  freezeCustomerReturnAttempt,
  isCustomerReturnAdapterReady,
  isCustomerReturnContextCurrent,
  type CustomerReturnEndpointAdapter,
} from "./customerReturnApi";

describe("customer-return adapter boundary", () => {
  it("fails closed when the central endpoint adapter is absent", () => {
    expect(isCustomerReturnAdapterReady(null)).toBe(false);
    expect(isCustomerReturnAdapterReady(undefined)).toBe(false);
  });

  it("does not accept an adapter with unpublished operation identities", () => {
    const adapter = {
      operations: { read: "", request: "", approve: "", reject: "" },
    } as CustomerReturnEndpointAdapter;

    expect(isCustomerReturnAdapterReady(adapter)).toBe(false);
  });

  it("suppresses stale company or authority context and retries only an unknown frozen attempt", () => {
    expect(isCustomerReturnContextCurrent("company-a:authority-2", "company-a:authority-2")).toBe(true);
    expect(isCustomerReturnContextCurrent("company-a:authority-2", "company-b:authority-2")).toBe(false);
    expect(canRetryCustomerReturnAttempt({
      type: "unknown",
      binding: "company-a:authority-2",
      idempotencyKey: "stable-key",
    })).toBe(true);
    expect(canRetryCustomerReturnAttempt({ type: "failed", binding: "company-a:authority-2" })).toBe(false);
  });

  it("freezes the idempotency key and exact body used by an unknown retry", () => {
    const attempt = freezeCustomerReturnAttempt({
      operationId: "central-operation-id",
      binding: "company-a:authority-2",
      idempotencyKey: "stable-key",
      payload: {
        customerReturnId: "00000000-0000-4000-8000-000000000001",
        sourceDeliveryId: "00000000-0000-4000-8000-000000000002",
        expectedInstructionVersion: "8",
        quantity: "1.250",
        reason: "Ambalaj hasarı",
        occurredAtUtc: "2026-10-04T08:00:00Z",
      },
    });

    expect(Object.isFrozen(attempt)).toBe(true);
    expect(Object.isFrozen(attempt.payload)).toBe(true);
    expect(attempt.idempotencyKey).toBe("stable-key");
    expect(attempt.payload).toEqual({
      customerReturnId: "00000000-0000-4000-8000-000000000001",
      sourceDeliveryId: "00000000-0000-4000-8000-000000000002",
      expectedInstructionVersion: "8",
      quantity: "1.250",
      reason: "Ambalaj hasarı",
      occurredAtUtc: "2026-10-04T08:00:00Z",
    });
  });
});
