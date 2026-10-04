import type {
  ApproveCustomerReturnV1,
  CustomerReturnResponseV1,
  CustomerReturnViewV1,
  RejectCustomerReturnV1,
  RequestCustomerReturnV1,
} from "./customerReturnContract";
import {
  decodeCustomerReturnListV1,
  decodeCustomerReturnResponseV1,
} from "./customerReturnContract";
import { API_BASE } from "./api";

export interface CustomerReturnContext {
  companyId: string;
  salesOrderId: string;
  loadingInstructionId: string;
  loadingInstructionLineId: string;
  binding: string;
}

export interface CustomerReturnOperationIds {
  read: string;
  request: string;
  approve: string;
  reject: string;
}

export interface FrozenCustomerReturnAttempt<TPayload> {
  operationId: string;
  binding: string;
  idempotencyKey: string;
  payload: Readonly<TPayload>;
}

export function freezeCustomerReturnAttempt<TPayload extends object>(
  attempt: FrozenCustomerReturnAttempt<TPayload>,
): FrozenCustomerReturnAttempt<TPayload> {
  return Object.freeze({
    ...attempt,
    payload: Object.freeze({ ...attempt.payload }),
  });
}

export type CustomerReturnAttemptLifecycle<TPayload extends object> =
  | { type: "idle" }
  | { type: "sending"; attempt: FrozenCustomerReturnAttempt<TPayload> }
  | { type: "unknown"; attempt: FrozenCustomerReturnAttempt<TPayload> }
  | { type: "failed"; binding: string }
  | {
    type: "succeeded";
    attempt: FrozenCustomerReturnAttempt<TPayload>;
    response: CustomerReturnResponseV1;
  };

export type CustomerReturnCommandOutcome =
  | { type: "succeeded"; response: CustomerReturnResponseV1 }
  | { type: "http"; status: number }
  | { type: "offline" }
  | { type: "network-error" }
  | { type: "malformed" }
  | { type: "aborted" };

export type CustomerReturnReadOutcome =
  | { type: "succeeded"; returns: readonly CustomerReturnViewV1[] }
  | { type: "http"; status: number }
  | { type: "offline" }
  | { type: "network-error" }
  | { type: "malformed" }
  | { type: "aborted" };

export function beginCustomerReturnAttempt<TPayload extends object>(
  current: CustomerReturnAttemptLifecycle<TPayload>,
  attempt: FrozenCustomerReturnAttempt<TPayload>,
): CustomerReturnAttemptLifecycle<TPayload> {
  if (current.type === "sending" || current.type === "unknown") {
    throw new Error("A live customer-return attempt must be retried or explicitly abandoned.");
  }
  if (
    attempt.operationId.trim().length === 0
    || attempt.binding.trim().length === 0
    || attempt.idempotencyKey.trim().length === 0
  ) {
    throw new Error("A customer-return attempt requires its published operation, binding and idempotency key.");
  }

  return { type: "sending", attempt: freezeCustomerReturnAttempt(attempt) };
}

export function settleCustomerReturnAttempt<TPayload extends object>(
  current: CustomerReturnAttemptLifecycle<TPayload>,
  attempt: FrozenCustomerReturnAttempt<TPayload>,
  outcome: CustomerReturnCommandOutcome,
  currentBinding: string,
): CustomerReturnAttemptLifecycle<TPayload> {
  if (
    current.type !== "sending"
    || current.attempt !== attempt
    || current.attempt.binding !== currentBinding
  ) {
    return current;
  }

  if (outcome.type === "succeeded") {
    return { type: "succeeded", attempt: current.attempt, response: outcome.response };
  }
  if (outcome.type === "http") {
    if (outcome.status >= 500 || (outcome.status >= 200 && outcome.status < 300)) {
      return { type: "unknown", attempt: current.attempt };
    }
    return { type: "failed", binding: current.attempt.binding };
  }

  return { type: "unknown", attempt: current.attempt };
}

export function retryCustomerReturnAttempt<TPayload extends object>(
  current: CustomerReturnAttemptLifecycle<TPayload>,
  currentBinding: string,
): CustomerReturnAttemptLifecycle<TPayload> {
  if (current.type !== "unknown" || current.attempt.binding !== currentBinding) {
    return current;
  }
  return { type: "sending", attempt: current.attempt };
}

export function abandonCustomerReturnAttempt<TPayload extends object>(
  current: CustomerReturnAttemptLifecycle<TPayload>,
  currentBinding: string,
): CustomerReturnAttemptLifecycle<TPayload> {
  return current.type === "unknown" && current.attempt.binding === currentBinding
    ? { type: "idle" }
    : current;
}

export function customerReturnCommandState<TPayload extends object>(
  lifecycle: CustomerReturnAttemptLifecycle<TPayload>,
): CustomerReturnCommandState {
  if (lifecycle.type === "idle") return lifecycle;
  if (lifecycle.type === "failed") return lifecycle;
  if (lifecycle.type === "sending") {
    return { type: "sending", binding: lifecycle.attempt.binding };
  }
  if (lifecycle.type === "unknown") {
    return {
      type: "unknown",
      binding: lifecycle.attempt.binding,
      idempotencyKey: lifecycle.attempt.idempotencyKey,
    };
  }
  return {
    type: "succeeded",
    binding: lifecycle.attempt.binding,
    response: lifecycle.response,
  };
}

export interface CustomerReturnEndpointAdapter {
  readonly operations: CustomerReturnOperationIds;
  read(context: CustomerReturnContext, signal: AbortSignal): Promise<CustomerReturnReadOutcome>;
  request(
    context: CustomerReturnContext,
    attempt: FrozenCustomerReturnAttempt<RequestCustomerReturnV1>,
    signal: AbortSignal,
  ): Promise<CustomerReturnCommandOutcome>;
  approve(
    context: CustomerReturnContext,
    customerReturnId: string,
    attempt: FrozenCustomerReturnAttempt<ApproveCustomerReturnV1>,
    signal: AbortSignal,
  ): Promise<CustomerReturnCommandOutcome>;
  reject(
    context: CustomerReturnContext,
    customerReturnId: string,
    attempt: FrozenCustomerReturnAttempt<RejectCustomerReturnV1>,
    signal: AbortSignal,
  ): Promise<CustomerReturnCommandOutcome>;
}

export type CustomerReturnReadState =
  | { type: "unavailable" }
  | { type: "loading"; binding: string }
  | { type: "failed"; binding: string }
  | { type: "ready"; binding: string; returns: readonly CustomerReturnViewV1[] };

export type CustomerReturnCommandState =
  | { type: "idle" }
  | { type: "sending"; binding: string }
  | { type: "unknown"; binding: string; idempotencyKey: string }
  | { type: "failed"; binding: string }
  | { type: "succeeded"; binding: string; response: CustomerReturnResponseV1 };

export interface CustomerReturnRequestDraft {
  sourceDeliveryId: string;
  expectedInstructionVersion: string;
  quantity: string;
  reason: string;
  occurredAtUtc: string;
}

export interface CustomerReturnDecisionDraft {
  customerReturnId: string;
  expectedVersion: string;
  action: "approve" | "reject";
  note: string | null;
}

export interface CustomerReturnRequestController {
  readonly state: CustomerReturnCommandState;
  submit(draft: CustomerReturnRequestDraft): void;
  retrySameAttempt(): void;
  abandonUnknownAttempt(): void;
}

export interface CustomerReturnDecisionController {
  readonly state: CustomerReturnCommandState;
  submit(draft: CustomerReturnDecisionDraft): void;
  retrySameAttempt(): void;
  abandonUnknownAttempt(): void;
}

export function isCustomerReturnAdapterReady(
  adapter: CustomerReturnEndpointAdapter | null | undefined,
): adapter is CustomerReturnEndpointAdapter {
  if (adapter === null || adapter === undefined) {
    return false;
  }

  const operations = adapter.operations;
  return [operations.read, operations.request, operations.approve, operations.reject]
    .every((operation) => operation.trim().length > 0);
}

export function isCustomerReturnContextCurrent(
  expectedBinding: string,
  actualBinding: string | undefined,
): boolean {
  return expectedBinding.length > 0 && expectedBinding === actualBinding;
}

export function canRetryCustomerReturnAttempt(state: CustomerReturnCommandState): boolean {
  return state.type === "unknown";
}

export function resetSettledCustomerReturnAttempt<TPayload extends object>(
  current: CustomerReturnAttemptLifecycle<TPayload>,
  currentBinding: string,
  freshReturns: readonly CustomerReturnViewV1[],
): CustomerReturnAttemptLifecycle<TPayload> {
  if (current.type !== "succeeded" || current.attempt.binding !== currentBinding) return current;
  const recorded = current.response.return;
  return freshReturns.some((item) =>
    item.customerReturnId === recorded.customerReturnId
    && item.status === recorded.status
    && item.version === recorded.version)
    ? { type: "idle" }
    : current;
}

export const CUSTOMER_RETURN_CAPABILITY = Object.freeze({
  read: "logistics.customer-returns.read",
  request: "logistics.customer-returns.request",
  approve: "logistics.customer-returns.approve",
  reject: "logistics.customer-returns.reject",
});

export const CUSTOMER_RETURN_OPERATION = Object.freeze({
  read: "GetLogisticsOrderCustomerReturnsV1",
  request: "RequestLogisticsCustomerReturnV1",
  approve: "ApproveLogisticsCustomerReturnV1",
  reject: "RejectLogisticsCustomerReturnV1",
});

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? "company-a-user";
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === "true";

function transportHeaders(companyId: string, idempotencyKey?: string): Record<string, string> {
  return {
    ...(DEV_IDENTITY_ENABLED ? { "X-Development-User": DEV_USER } : {}),
    "X-Company-Id": companyId,
    ...(idempotencyKey === undefined
      ? {}
      : { "Content-Type": "application/json", "Idempotency-Key": idempotencyKey }),
  };
}

function transportFailure(signal: AbortSignal): Exclude<CustomerReturnCommandOutcome, { type: "succeeded" } | { type: "http" }> {
  if (signal.aborted) return { type: "aborted" };
  if (globalThis.navigator?.onLine === false) return { type: "offline" };
  return { type: "network-error" };
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    return null;
  }
}

function responseMatchesHeaders(response: Response, decoded: CustomerReturnResponseV1): boolean {
  const replay = response.headers.get("Idempotency-Replayed");
  if (replay !== null && replay !== "true") return false;
  if ((replay === "true") !== decoded.isReplay) return false;
  return !decoded.isReplay
    || response.headers.get("Idempotency-Original-Correlation-Id") === decoded.originalCorrelationId;
}

async function command(
  context: CustomerReturnContext,
  path: string,
  expectedStatus: number,
  expectedState: "Pending" | "Approved" | "Rejected",
  customerReturnId: string,
  attempt: FrozenCustomerReturnAttempt<object>,
  signal: AbortSignal,
): Promise<CustomerReturnCommandOutcome> {
  let response: Response;
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: "POST",
      headers: transportHeaders(context.companyId, attempt.idempotencyKey),
      body: JSON.stringify(attempt.payload),
      signal,
    });
  } catch {
    return transportFailure(signal);
  }

  const body = await readJson(response);
  if (response.status !== expectedStatus) return { type: "http", status: response.status };
  const decoded = decodeCustomerReturnResponseV1(body, {
    companyId: context.companyId,
    loadingInstructionId: context.loadingInstructionId,
    loadingInstructionLineId: context.loadingInstructionLineId,
  });
  if (
    !decoded.ok
    || decoded.value.return.customerReturnId !== customerReturnId
    || decoded.value.return.status !== expectedState
    || !responseMatchesHeaders(response, decoded.value)
  ) {
    return { type: "malformed" };
  }
  return { type: "succeeded", response: decoded.value };
}

export const customerReturnEndpointAdapter: CustomerReturnEndpointAdapter = {
  operations: CUSTOMER_RETURN_OPERATION,
  async read(context, signal) {
    let response: Response;
    try {
      response = await fetch(`${API_BASE}/logistics/orders/${context.salesOrderId}/customer-returns`, {
        headers: transportHeaders(context.companyId),
        signal,
      });
    } catch {
      return transportFailure(signal);
    }
    const body = await readJson(response);
    if (response.status !== 200) return { type: "http", status: response.status };
    const decoded = decodeCustomerReturnListV1(body, {
      companyId: context.companyId,
    });
    if (!decoded.ok || decoded.value.some((item) => item.sourceOrderId !== context.salesOrderId)) {
      return { type: "malformed" };
    }
    return {
      type: "succeeded",
      returns: decoded.value.filter((item) =>
        item.loadingInstructionId === context.loadingInstructionId
        && item.loadingInstructionLineId === context.loadingInstructionLineId),
    };
  },
  request(context, attempt, signal) {
    return command(
      context,
      `/logistics/instructions/${context.loadingInstructionId}/lines/${context.loadingInstructionLineId}/customer-returns`,
      201,
      "Pending",
      attempt.payload.customerReturnId,
      attempt,
      signal,
    );
  },
  approve(context, customerReturnId, attempt, signal) {
    return command(
      context,
      `/logistics/customer-returns/${customerReturnId}/approve`,
      200,
      "Approved",
      customerReturnId,
      attempt,
      signal,
    );
  },
  reject(context, customerReturnId, attempt, signal) {
    return command(
      context,
      `/logistics/customer-returns/${customerReturnId}/reject`,
      200,
      "Rejected",
      customerReturnId,
      attempt,
      signal,
    );
  },
};
