import { afterEach, describe, expect, it, vi } from "vitest";
import {
  abandonCustomerReturnAttempt,
  beginCustomerReturnAttempt,
  canRetryCustomerReturnAttempt,
  customerReturnCommandState,
  freezeCustomerReturnAttempt,
  isCustomerReturnAdapterReady,
  isCustomerReturnContextCurrent,
  retryCustomerReturnAttempt,
  resetSettledCustomerReturnAttempt,
  settleCustomerReturnAttempt,
  customerReturnEndpointAdapter,
  type CustomerReturnEndpointAdapter,
  type CustomerReturnAttemptLifecycle,
} from "./customerReturnApi";
import type { RequestCustomerReturnV1 } from "./customerReturnContract";

const requestPayload: RequestCustomerReturnV1 = {
  customerReturnId: "00000000-0000-4000-8000-000000000001",
  sourceDeliveryId: "00000000-0000-4000-8000-000000000002",
  expectedInstructionVersion: "8",
  quantity: "1.250",
  reason: "Ambalaj hasarı",
  occurredAtUtc: "2026-10-04T08:00:00Z",
};

function requestAttempt() {
  return {
    operationId: "central-operation-id",
    binding: "company-a:authority-2",
    idempotencyKey: "stable-key",
    payload: requestPayload,
  };
}

describe("customer-return adapter boundary", () => {
  afterEach(() => vi.unstubAllGlobals());

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
    const attempt = freezeCustomerReturnAttempt(requestAttempt());

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

  it("retries an unknown transport result with the same frozen key and body", () => {
    const sending = beginCustomerReturnAttempt({ type: "idle" }, requestAttempt());
    expect(sending.type).toBe("sending");
    if (sending.type !== "sending") throw new Error("expected sending state");

    const unknown = settleCustomerReturnAttempt(
      sending,
      sending.attempt,
      { type: "network-error" },
      sending.attempt.binding,
    );
    expect(customerReturnCommandState(unknown)).toEqual({
      type: "unknown",
      binding: "company-a:authority-2",
      idempotencyKey: "stable-key",
    });

    const retry = retryCustomerReturnAttempt(unknown, "company-a:authority-2");
    expect(retry.type).toBe("sending");
    if (retry.type !== "sending") throw new Error("expected retry state");
    expect(retry.attempt).toBe(sending.attempt);
    expect(retry.attempt.payload).toEqual(requestPayload);
  });

  it("does not start a quiet new key while an unknown attempt is live", () => {
    const frozen = freezeCustomerReturnAttempt(requestAttempt());
    const unknown: CustomerReturnAttemptLifecycle<RequestCustomerReturnV1> = { type: "unknown", attempt: frozen };

    expect(() => beginCustomerReturnAttempt(unknown, {
      ...requestAttempt(),
      idempotencyKey: "different-key",
    })).toThrow(/retried or explicitly abandoned/i);
  });

  it("ignores a late result and refuses retry or abandon after the company-authority binding changes", () => {
    const sending = beginCustomerReturnAttempt({ type: "idle" }, requestAttempt());
    if (sending.type !== "sending") throw new Error("expected sending state");

    const late = settleCustomerReturnAttempt(
      sending,
      sending.attempt,
      { type: "http", status: 409 },
      "company-b:authority-2",
    );
    expect(late).toBe(sending);

    const unknown: CustomerReturnAttemptLifecycle<RequestCustomerReturnV1> = {
      type: "unknown",
      attempt: sending.attempt,
    };
    expect(retryCustomerReturnAttempt(unknown, "company-b:authority-2")).toBe(unknown);
    expect(abandonCustomerReturnAttempt(unknown, "company-b:authority-2")).toBe(unknown);
  });

  it("treats a definitive HTTP response as failed before allowing a deliberate new attempt", () => {
    const sending = beginCustomerReturnAttempt({ type: "idle" }, requestAttempt());
    if (sending.type !== "sending") throw new Error("expected sending state");

    const failed = settleCustomerReturnAttempt(
      sending,
      sending.attempt,
      { type: "http", status: 409 },
      sending.attempt.binding,
    );
    expect(customerReturnCommandState(failed)).toEqual({
      type: "failed",
      binding: "company-a:authority-2",
    });

    const next = beginCustomerReturnAttempt(failed, {
      ...requestAttempt(),
      idempotencyKey: "deliberate-new-key",
    });
    expect(next.type).toBe("sending");
    if (next.type !== "sending") throw new Error("expected next attempt");
    expect(next.attempt.idempotencyKey).toBe("deliberate-new-key");
  });

  it("keeps a 5xx attempt frozen and retries the same key and body into one successful replay", async () => {
    const context = {
      companyId: "00000000-0000-4000-8000-000000000003",
      salesOrderId: "00000000-0000-4000-8000-000000000007",
      loadingInstructionId: "00000000-0000-4000-8000-000000000004",
      loadingInstructionLineId: "00000000-0000-4000-8000-000000000005",
      binding: "company-a:authority-2",
    };
    const response = {
      return: {
        customerReturnId: requestPayload.customerReturnId,
        companyId: context.companyId,
        loadingInstructionId: context.loadingInstructionId,
        loadingInstructionLineId: context.loadingInstructionLineId,
        sourceDeliveryId: requestPayload.sourceDeliveryId,
        sourceShipmentId: "00000000-0000-4000-8000-000000000006",
        sourceOrderId: context.salesOrderId,
        sourceOrderLineId: "00000000-0000-4000-8000-000000000008",
        unitId: "00000000-0000-4000-8000-000000000009",
        quantity: requestPayload.quantity,
        sourceDeliveredQuantity: "2.000",
        status: "Pending",
        version: "1",
        reason: requestPayload.reason,
        requestedBy: "actor-a",
        occurredAtUtc: requestPayload.occurredAtUtc,
        requestedAtUtc: "2026-10-04T08:01:00Z",
        decidedBy: null,
        decidedAtUtc: null,
        decisionNote: null,
        inventoryMovementId: null,
        originalLabelId: null,
        originalShipmentMovementId: null,
      },
      isReplay: true,
      originalCorrelationId: "1234567890abcdef1234567890abcdef",
    };
    let effects = 0;
    let calls = 0;
    vi.stubGlobal("fetch", vi.fn(async () => {
      calls += 1;
      if (calls === 1) {
        effects += 1;
        return new Response(JSON.stringify({ status: 500 }), { status: 500 });
      }
      return new Response(JSON.stringify(response), {
        status: 201,
        headers: { "Content-Type": "application/json", "Idempotency-Replayed": "true" },
      });
    }));

    const sending = beginCustomerReturnAttempt({ type: "idle" }, requestAttempt());
    if (sending.type !== "sending") throw new Error("expected sending state");
    const first = await customerReturnEndpointAdapter.request(context, sending.attempt, new AbortController().signal);
    const unknown = settleCustomerReturnAttempt(sending, sending.attempt, first, context.binding);
    expect(unknown).toEqual({ type: "unknown", attempt: sending.attempt });
    const retry = retryCustomerReturnAttempt(unknown, context.binding);
    if (retry.type !== "sending") throw new Error("expected retry state");
    const replay = await customerReturnEndpointAdapter.request(context, retry.attempt, new AbortController().signal);
    expect(replay).toEqual({ type: "succeeded", response });

    const posts = vi.mocked(fetch).mock.calls;
    expect(posts).toHaveLength(2);
    expect((posts[0][1]?.headers as Record<string, string>)["Idempotency-Key"])
      .toBe((posts[1][1]?.headers as Record<string, string>)["Idempotency-Key"]);
    expect(posts[0][1]?.body).toBe(posts[1][1]?.body);
    expect(effects).toBe(1);
  });

  it("opens a new partial-return attempt only after a fresh read observes the completed return", () => {
    const sending = beginCustomerReturnAttempt({ type: "idle" }, requestAttempt());
    if (sending.type !== "sending") throw new Error("expected sending state");
    const response = {
      return: {
        customerReturnId: requestPayload.customerReturnId,
        companyId: "00000000-0000-4000-8000-000000000003",
        loadingInstructionId: "00000000-0000-4000-8000-000000000004",
        loadingInstructionLineId: "00000000-0000-4000-8000-000000000005",
        sourceDeliveryId: requestPayload.sourceDeliveryId,
        sourceShipmentId: "00000000-0000-4000-8000-000000000006",
        sourceOrderId: "00000000-0000-4000-8000-000000000007",
        sourceOrderLineId: "00000000-0000-4000-8000-000000000008",
        unitId: "00000000-0000-4000-8000-000000000009",
        quantity: requestPayload.quantity,
        sourceDeliveredQuantity: "2.000",
        status: "Pending" as const,
        version: "1",
        reason: requestPayload.reason,
        requestedBy: "actor-a",
        occurredAtUtc: requestPayload.occurredAtUtc,
        requestedAtUtc: "2026-10-04T08:01:00Z",
        decidedBy: null,
        decidedAtUtc: null,
        decisionNote: null,
        inventoryMovementId: null,
        originalLabelId: null,
        originalShipmentMovementId: null,
      },
      isReplay: false,
      originalCorrelationId: "opaque-first-attempt",
    };
    const succeeded = settleCustomerReturnAttempt(
      sending,
      sending.attempt,
      { type: "succeeded", response },
      sending.attempt.binding,
    );
    expect(resetSettledCustomerReturnAttempt(succeeded, sending.attempt.binding, [])).toBe(succeeded);
    const idle = resetSettledCustomerReturnAttempt(succeeded, sending.attempt.binding, [response.return]);
    expect(idle).toEqual({ type: "idle" });
    const next = beginCustomerReturnAttempt(idle, { ...requestAttempt(), idempotencyKey: "second-partial-key" });
    expect(next.type === "sending" && next.attempt.idempotencyKey).toBe("second-partial-key");
  });

  it("keeps the immutable replay response when the exact live attempt succeeds", () => {
    const sending = beginCustomerReturnAttempt({ type: "idle" }, requestAttempt());
    if (sending.type !== "sending") throw new Error("expected sending state");
    const response = {
      return: {
        customerReturnId: requestPayload.customerReturnId,
        companyId: "00000000-0000-4000-8000-000000000003",
        loadingInstructionId: "00000000-0000-4000-8000-000000000004",
        loadingInstructionLineId: "00000000-0000-4000-8000-000000000005",
        sourceDeliveryId: requestPayload.sourceDeliveryId,
        sourceShipmentId: "00000000-0000-4000-8000-000000000006",
        sourceOrderId: "00000000-0000-4000-8000-000000000007",
        sourceOrderLineId: "00000000-0000-4000-8000-000000000008",
        unitId: "00000000-0000-4000-8000-000000000009",
        quantity: requestPayload.quantity,
        sourceDeliveredQuantity: "2.000",
        status: "Pending" as const,
        version: "1",
        reason: requestPayload.reason,
        requestedBy: "actor-a",
        occurredAtUtc: requestPayload.occurredAtUtc,
        requestedAtUtc: "2026-10-04T08:01:00Z",
        decidedBy: null,
        decidedAtUtc: null,
        decisionNote: null,
        inventoryMovementId: null,
        originalLabelId: null,
        originalShipmentMovementId: null,
      },
      isReplay: true,
      originalCorrelationId: "00000000-0000-4000-8000-000000000010",
    };

    const succeeded = settleCustomerReturnAttempt(
      sending,
      sending.attempt,
      { type: "succeeded", response },
      sending.attempt.binding,
    );
    expect(customerReturnCommandState(succeeded)).toEqual({
      type: "succeeded",
      binding: "company-a:authority-2",
      response,
    });
  });

  it("reads the published order route and suppresses returns from another instruction line", async () => {
    const accepted = {
      customerReturnId: "00000000-0000-4000-8000-000000000011",
      companyId: "00000000-0000-4000-8000-000000000003",
      loadingInstructionId: "00000000-0000-4000-8000-000000000004",
      loadingInstructionLineId: "00000000-0000-4000-8000-000000000005",
      sourceDeliveryId: requestPayload.sourceDeliveryId,
      sourceShipmentId: "00000000-0000-4000-8000-000000000006",
      sourceOrderId: "00000000-0000-4000-8000-000000000007",
      sourceOrderLineId: "00000000-0000-4000-8000-000000000008",
      unitId: "00000000-0000-4000-8000-000000000009",
      quantity: "1", sourceDeliveredQuantity: "2", status: "Pending", version: "1",
      reason: "İade", requestedBy: "actor-a", occurredAtUtc: "2026-10-04T08:00:00Z",
      requestedAtUtc: "2026-10-04T08:01:00Z", decidedBy: null, decidedAtUtc: null,
      decisionNote: null, inventoryMovementId: null, originalLabelId: null, originalShipmentMovementId: null,
    };
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify([
      accepted,
      { ...accepted, customerReturnId: "00000000-0000-4000-8000-000000000012", loadingInstructionLineId: "00000000-0000-4000-8000-000000000099" },
    ]), { status: 200, headers: { "Content-Type": "application/json" } })));

    const outcome = await customerReturnEndpointAdapter.read({
      companyId: accepted.companyId,
      salesOrderId: accepted.sourceOrderId,
      loadingInstructionId: accepted.loadingInstructionId,
      loadingInstructionLineId: accepted.loadingInstructionLineId,
      binding: "binding",
    }, new AbortController().signal);

    expect(outcome).toEqual({ type: "succeeded", returns: [accepted] });
    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining(`/logistics/orders/${accepted.sourceOrderId}/customer-returns`),
      expect.objectContaining({ headers: expect.objectContaining({ "X-Company-Id": accepted.companyId }) }),
    );
  });
});
