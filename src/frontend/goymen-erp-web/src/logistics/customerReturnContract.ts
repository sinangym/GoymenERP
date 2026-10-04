export type CustomerReturnStatus = "Pending" | "Approved" | "Rejected";

export interface RequestCustomerReturnV1 {
  customerReturnId: string;
  sourceDeliveryId: string;
  expectedInstructionVersion: string;
  quantity: string;
  reason: string;
  occurredAtUtc: string;
}

export interface ApproveCustomerReturnV1 {
  expectedVersion: string;
  inventoryMovementId: string;
  note?: string | null;
}

export interface RejectCustomerReturnV1 {
  expectedVersion: string;
  note?: string | null;
}

export interface CustomerReturnViewV1 {
  customerReturnId: string;
  companyId: string;
  loadingInstructionId: string;
  loadingInstructionLineId: string;
  sourceDeliveryId: string;
  sourceShipmentId: string;
  sourceOrderId: string;
  sourceOrderLineId: string;
  unitId: string;
  quantity: string;
  sourceDeliveredQuantity: string;
  status: CustomerReturnStatus;
  version: string;
  reason: string;
  requestedBy: string;
  occurredAtUtc: string;
  requestedAtUtc: string;
  decidedBy: string | null;
  decidedAtUtc: string | null;
  decisionNote: string | null;
  inventoryMovementId: string | null;
  originalLabelId: string | null;
  originalShipmentMovementId: string | null;
}

export interface CustomerReturnResponseV1 {
  return: CustomerReturnViewV1;
  isReplay: boolean;
  originalCorrelationId: string;
}

export interface CustomerReturnDecodeExpectation {
  companyId?: string;
  loadingInstructionId?: string;
  loadingInstructionLineId?: string;
}

export type CustomerReturnDecodeResult<T> =
  | { ok: true; value: T }
  | { ok: false; error: string };

const returnKeys = [
  "customerReturnId",
  "companyId",
  "loadingInstructionId",
  "loadingInstructionLineId",
  "sourceDeliveryId",
  "sourceShipmentId",
  "sourceOrderId",
  "sourceOrderLineId",
  "unitId",
  "quantity",
  "sourceDeliveredQuantity",
  "status",
  "version",
  "reason",
  "requestedBy",
  "occurredAtUtc",
  "requestedAtUtc",
  "decidedBy",
  "decidedAtUtc",
  "decisionNote",
  "inventoryMovementId",
  "originalLabelId",
  "originalShipmentMovementId",
] as const;

const responseKeys = ["return", "isReplay", "originalCorrelationId"] as const;
const correlationIdMaximumLength = 128;
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const positiveDecimalPattern = /^(?:0*[1-9]\d*)(?:\.\d+)?$|^0*\.\d*[1-9]\d*$/;
const versionPattern = /^(?:0|[1-9]\d*)$/;

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort();
  const expected = [...keys].sort();
  return actual.length === expected.length && actual.every((key, index) => key === expected[index]);
}

function isUuid(value: unknown): value is string {
  return typeof value === "string" && uuidPattern.test(value);
}

function isBoundedText(value: unknown, maximumLength: number): value is string {
  return typeof value === "string"
    && value.length > 0
    && value.length <= maximumLength
    && value.trim().length > 0;
}

function isVersion(value: unknown): value is string {
  return typeof value === "string" && versionPattern.test(value);
}

function isPositiveDecimal(value: unknown): value is string {
  return typeof value === "string" && positiveDecimalPattern.test(value);
}

function isInstant(value: unknown): value is string {
  return typeof value === "string" && !Number.isNaN(Date.parse(value));
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === "string";
}

function isNullableUuid(value: unknown): value is string | null {
  return value === null || isUuid(value);
}

function isNullableInstant(value: unknown): value is string | null {
  return value === null || isInstant(value);
}

function statusShapeIsCoherent(value: CustomerReturnViewV1): boolean {
  if (value.status === "Pending") {
    return value.version === "1"
      && value.decidedBy === null
      && value.decidedAtUtc === null
      && value.decisionNote === null
      && value.inventoryMovementId === null
      && value.originalLabelId === null
      && value.originalShipmentMovementId === null;
  }

  if (value.status === "Rejected") {
    return value.version === "2"
      && value.decidedBy !== null
      && value.decidedAtUtc !== null
      && value.inventoryMovementId === null
      && value.originalLabelId === null
      && value.originalShipmentMovementId === null;
  }

  return value.version === "2"
    && value.decidedBy !== null
    && value.decidedAtUtc !== null
    && value.inventoryMovementId !== null
    && value.originalLabelId !== null
    && value.originalShipmentMovementId !== null;
}

export function decodeCustomerReturnViewV1(
  input: unknown,
  expected: CustomerReturnDecodeExpectation = {},
): CustomerReturnDecodeResult<CustomerReturnViewV1> {
  if (!isObject(input) || !hasExactKeys(input, returnKeys)) {
    return { ok: false, error: "customer-return-shape" };
  }

  const status = input.status;
  if (status !== "Pending" && status !== "Approved" && status !== "Rejected") {
    return { ok: false, error: "customer-return-status" };
  }

  if (
    !isUuid(input.customerReturnId)
    || !isUuid(input.companyId)
    || !isUuid(input.loadingInstructionId)
    || !isUuid(input.loadingInstructionLineId)
    || !isUuid(input.sourceDeliveryId)
    || !isUuid(input.sourceShipmentId)
    || !isUuid(input.sourceOrderId)
    || !isUuid(input.sourceOrderLineId)
    || !isUuid(input.unitId)
    || !isPositiveDecimal(input.quantity)
    || !isPositiveDecimal(input.sourceDeliveredQuantity)
    || !isVersion(input.version)
    || typeof input.reason !== "string"
    || input.reason.trim().length === 0
    || typeof input.requestedBy !== "string"
    || input.requestedBy.trim().length === 0
    || !isInstant(input.occurredAtUtc)
    || !isInstant(input.requestedAtUtc)
    || !isNullableString(input.decidedBy)
    || !isNullableInstant(input.decidedAtUtc)
    || !isNullableString(input.decisionNote)
    || !isNullableUuid(input.inventoryMovementId)
    || !isNullableUuid(input.originalLabelId)
    || !isNullableUuid(input.originalShipmentMovementId)
  ) {
    return { ok: false, error: "customer-return-member" };
  }

  const value = input as unknown as CustomerReturnViewV1;
  if (
    (expected.companyId !== undefined && value.companyId !== expected.companyId)
    || (expected.loadingInstructionId !== undefined && value.loadingInstructionId !== expected.loadingInstructionId)
    || (expected.loadingInstructionLineId !== undefined && value.loadingInstructionLineId !== expected.loadingInstructionLineId)
  ) {
    return { ok: false, error: "customer-return-context" };
  }

  if (!statusShapeIsCoherent(value)) {
    return { ok: false, error: "customer-return-state" };
  }

  return { ok: true, value };
}

export function decodeCustomerReturnResponseV1(
  input: unknown,
  expected: CustomerReturnDecodeExpectation = {},
): CustomerReturnDecodeResult<CustomerReturnResponseV1> {
  if (!isObject(input) || !hasExactKeys(input, responseKeys)) {
    return { ok: false, error: "customer-return-response-shape" };
  }

  const decodedReturn = decodeCustomerReturnViewV1(input.return, expected);
  if (
    !decodedReturn.ok
    || typeof input.isReplay !== "boolean"
    || !isBoundedText(input.originalCorrelationId, correlationIdMaximumLength)
  ) {
    return { ok: false, error: decodedReturn.ok ? "customer-return-response-member" : decodedReturn.error };
  }

  return {
    ok: true,
    value: {
      return: decodedReturn.value,
      isReplay: input.isReplay,
      originalCorrelationId: input.originalCorrelationId,
    },
  };
}

export function decodeCustomerReturnListV1(
  input: unknown,
  expected: CustomerReturnDecodeExpectation = {},
): CustomerReturnDecodeResult<readonly CustomerReturnViewV1[]> {
  if (!Array.isArray(input)) {
    return { ok: false, error: "customer-return-list-shape" };
  }

  const values: CustomerReturnViewV1[] = [];
  for (const item of input) {
    const decoded = decodeCustomerReturnViewV1(item, expected);
    if (!decoded.ok) {
      return decoded;
    }
    values.push(decoded.value);
  }

  return { ok: true, value: values };
}
