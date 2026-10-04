import { useState, type FormEvent } from "react";
import type { CustomerReturnStatus } from "./customerReturnContract";
import {
  canRetryCustomerReturnAttempt,
  isCustomerReturnContextCurrent,
  type CustomerReturnDecisionController,
  type CustomerReturnReadState,
  type CustomerReturnRequestController,
} from "./customerReturnApi";
import { customerReturnText as text } from "./customerReturnText";

export interface CustomerReturnVisibility {
  canRead: boolean;
  canRequest: boolean;
  canApprove: boolean;
  canReject: boolean;
}

export interface CustomerReturnDeliveryOption {
  sourceDeliveryId: string;
  label: string;
  deliveredQuantity: string;
  unitId: string;
}

export interface CustomerReturnPanelProps {
  available: boolean;
  contextBinding: string;
  instructionVersion: string;
  visibility: CustomerReturnVisibility;
  readState: CustomerReturnReadState;
  deliveryOptions: readonly CustomerReturnDeliveryOption[];
  requestController?: CustomerReturnRequestController;
  decisionController?: CustomerReturnDecisionController;
}

function statusLabel(status: CustomerReturnStatus): string {
  if (status === "Approved") return text.approved;
  if (status === "Rejected") return text.rejected;
  return text.pending;
}

function CommandStateNotice({
  controller,
}: {
  controller: CustomerReturnRequestController | CustomerReturnDecisionController;
}) {
  const state = controller.state;
  if (state.type === "idle") return null;
  if (state.type === "sending") return <p role="status">{text.sending}</p>;
  if (state.type === "failed") return <p role="alert">{text.commandFailed}</p>;
  if (state.type === "succeeded") return <p role="status">{text.commandSucceeded}</p>;

  return (
    <div role="alert">
      <p>{text.unknown}</p>
      <button type="button" onClick={controller.retrySameAttempt}>{text.retry}</button>
      <button type="button" onClick={controller.abandonUnknownAttempt}>{text.abandon}</button>
    </div>
  );
}

export function CustomerReturnPanel({
  available,
  contextBinding,
  instructionVersion,
  visibility,
  readState,
  deliveryOptions,
  requestController,
  decisionController,
}: CustomerReturnPanelProps) {
  const [deliveryId, setDeliveryId] = useState(deliveryOptions[0]?.sourceDeliveryId ?? "");
  const [quantity, setQuantity] = useState("");
  const [reason, setReason] = useState("");
  const [occurredAtUtc, setOccurredAtUtc] = useState("");
  const [decisionNotes, setDecisionNotes] = useState<Record<string, string>>({});
  const requestLocked = requestController !== undefined
    && requestController.state.type !== "idle"
    && requestController.state.type !== "failed";
  const decisionLocked = decisionController !== undefined
    && decisionController.state.type !== "idle"
    && decisionController.state.type !== "failed";

  if (!available) {
    return <section aria-label={text.title}><p>{text.unavailable}</p></section>;
  }

  if (!visibility.canRead) {
    return <section aria-label={text.title}><p>{text.neutralNotFound}</p></section>;
  }

  if (readState.type === "unavailable") {
    return <section aria-label={text.title}><p>{text.unavailable}</p></section>;
  }

  if (!isCustomerReturnContextCurrent(contextBinding, readState.binding)) {
    return <section aria-label={text.title}><p>{text.neutralNotFound}</p></section>;
  }

  if (readState.type === "loading") {
    return <section aria-label={text.title}><p role="status">{text.loading}</p></section>;
  }

  if (readState.type === "failed") {
    return <section aria-label={text.title}><p role="alert">{text.failed}</p></section>;
  }

  const submitRequest = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!requestController || !visibility.canRequest || deliveryId.length === 0) return;
    if (requestController.state.type !== "idle" && requestController.state.type !== "failed") return;

    requestController.submit({
      sourceDeliveryId: deliveryId,
      expectedInstructionVersion: instructionVersion,
      quantity: quantity.trim(),
      reason: reason.trim(),
      occurredAtUtc: new Date(occurredAtUtc).toISOString(),
    });
  };

  return (
    <section aria-label={text.title}>
      <h2>{text.title}</h2>

      {readState.returns.length === 0 ? <p>{text.empty}</p> : (
        <ul>
          {readState.returns.map((customerReturn) => (
            <li key={customerReturn.customerReturnId}>
              <p>{customerReturn.quantity} - {statusLabel(customerReturn.status)}</p>
              <p>{customerReturn.reason}</p>
              {customerReturn.status === "Pending" && (visibility.canApprove || visibility.canReject) && decisionController ? (
                <div>
                  <label>
                    {text.note}
                    <input
                      value={decisionNotes[customerReturn.customerReturnId] ?? ""}
                      onChange={(event) => setDecisionNotes((current) => ({
                        ...current,
                        [customerReturn.customerReturnId]: event.target.value,
                      }))}
                    />
                  </label>
                  {visibility.canApprove ? (
                    <button
                      type="button"
                      disabled={decisionLocked}
                      onClick={() => decisionController.submit({
                        customerReturnId: customerReturn.customerReturnId,
                        expectedVersion: customerReturn.version,
                        action: "approve",
                        note: decisionNotes[customerReturn.customerReturnId]?.trim() || null,
                      })}
                    >
                      {text.approve}
                    </button>
                  ) : null}
                  {visibility.canReject ? (
                    <button
                      type="button"
                      disabled={decisionLocked}
                      onClick={() => decisionController.submit({
                        customerReturnId: customerReturn.customerReturnId,
                        expectedVersion: customerReturn.version,
                        action: "reject",
                        note: decisionNotes[customerReturn.customerReturnId]?.trim() || null,
                      })}
                    >
                      {text.reject}
                    </button>
                  ) : null}
                </div>
              ) : null}
            </li>
          ))}
        </ul>
      )}

      {decisionController && isCustomerReturnContextCurrent(contextBinding, decisionController.state.type === "idle" ? contextBinding : decisionController.state.binding)
        ? <CommandStateNotice controller={decisionController} />
        : null}

      {visibility.canRequest && requestController && deliveryOptions.length > 0 ? (
        <form onSubmit={submitRequest}>
          <h3>{text.requestTitle}</h3>
          <label>
            {text.delivery}
            <select value={deliveryId} onChange={(event) => setDeliveryId(event.target.value)} required>
              {deliveryOptions.map((option) => (
                <option key={option.sourceDeliveryId} value={option.sourceDeliveryId}>
                  {option.label} ({option.deliveredQuantity})
                </option>
              ))}
            </select>
          </label>
          <label>
            {text.quantity}
            <input value={quantity} onChange={(event) => setQuantity(event.target.value)} inputMode="decimal" required />
          </label>
          <label>
            {text.reason}
            <textarea value={reason} onChange={(event) => setReason(event.target.value)} required />
          </label>
          <label>
            {text.occurredAt}
            <input
              type="datetime-local"
              value={occurredAtUtc}
              onChange={(event) => setOccurredAtUtc(event.target.value)}
              required
            />
          </label>
          <button type="submit" disabled={requestLocked}>{text.request}</button>
          {isCustomerReturnContextCurrent(contextBinding, requestController.state.type === "idle" ? contextBinding : requestController.state.binding)
            ? <CommandStateNotice controller={requestController} />
            : null}
        </form>
      ) : null}
    </section>
  );
}

export function customerReturnAttemptIsFrozen(
  controller: CustomerReturnRequestController | CustomerReturnDecisionController | undefined,
): boolean {
  return controller !== undefined && canRetryCustomerReturnAttempt(controller.state);
}
