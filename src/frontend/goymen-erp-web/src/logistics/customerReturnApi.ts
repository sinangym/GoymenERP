import type {
  ApproveCustomerReturnV1,
  CustomerReturnResponseV1,
  CustomerReturnViewV1,
  RejectCustomerReturnV1,
  RequestCustomerReturnV1,
} from "./customerReturnContract";

export interface CustomerReturnContext {
  companyId: string;
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
