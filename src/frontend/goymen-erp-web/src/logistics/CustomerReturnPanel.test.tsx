import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { CustomerReturnViewV1 } from "./customerReturnContract";
import type { CustomerReturnDecisionController, CustomerReturnRequestController } from "./customerReturnApi";
import { CustomerReturnPanel } from "./CustomerReturnPanel";

const binding = "company-a:authority-1:instruction-1";
const pendingReturn: CustomerReturnViewV1 = {
  customerReturnId: "00000000-0000-4000-8000-000000000001",
  companyId: "00000000-0000-4000-8000-000000000002",
  loadingInstructionId: "00000000-0000-4000-8000-000000000003",
  loadingInstructionLineId: "00000000-0000-4000-8000-000000000004",
  sourceDeliveryId: "00000000-0000-4000-8000-000000000005",
  sourceShipmentId: "00000000-0000-4000-8000-000000000006",
  sourceOrderId: "00000000-0000-4000-8000-000000000007",
  sourceOrderLineId: "00000000-0000-4000-8000-000000000008",
  unitId: "00000000-0000-4000-8000-000000000009",
  quantity: "1.000",
  sourceDeliveredQuantity: "2.000",
  status: "Pending",
  version: "1",
  reason: "Ambalaj hasarı",
  requestedBy: "the-same-visible-user",
  occurredAtUtc: "2026-10-04T08:00:00Z",
  requestedAtUtc: "2026-10-04T08:01:00Z",
  decidedBy: null,
  decidedAtUtc: null,
  decisionNote: null,
  inventoryMovementId: null,
  originalLabelId: null,
  originalShipmentMovementId: null,
};

function baseProps() {
  return {
    available: true,
    contextBinding: binding,
    instructionVersion: "8",
    visibility: { canRead: true, canRequest: false, canApprove: false, canReject: false },
    readState: { type: "ready" as const, binding, returns: [pendingReturn] },
    deliveryOptions: [{
      sourceDeliveryId: pendingReturn.sourceDeliveryId,
      label: "Teslimat 1",
      deliveredQuantity: "2.000",
      unitId: pendingReturn.unitId,
    }],
  };
}

describe("CustomerReturnPanel", () => {
  it("fails closed without a published central adapter", () => {
    render(<CustomerReturnPanel {...baseProps()} available={false} />);
    expect(screen.getByText(/henüz kullanıma hazır değil/i)).not.toBeNull();
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("uses a neutral state and exposes no protected data without read visibility", () => {
    render(<CustomerReturnPanel
      {...baseProps()}
      visibility={{ canRead: false, canRequest: true, canApprove: true, canReject: true }}
    />);

    expect(screen.getByText(/kullanılabilir iade bilgisi bulunamadı/i)).not.toBeNull();
    expect(screen.queryByText("Ambalaj hasarı")).toBeNull();
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("does not infer decision authority from the visible requestedBy value", () => {
    render(<CustomerReturnPanel {...baseProps()} />);
    expect(screen.getByText("Ambalaj hasarı")).not.toBeNull();
    expect(screen.queryByRole("button", { name: "Onayla" })).toBeNull();
  });

  it("submits a decision only through the explicitly supplied authority controller", () => {
    const submit = vi.fn();
    const controller: CustomerReturnDecisionController = {
      state: { type: "idle" },
      submit,
      retrySameAttempt: vi.fn(),
      abandonUnknownAttempt: vi.fn(),
    };

    render(<CustomerReturnPanel
      {...baseProps()}
      visibility={{ canRead: true, canRequest: false, canApprove: true, canReject: false }}
      decisionController={controller}
    />);
    fireEvent.click(screen.getByRole("button", { name: "Onayla" }));

    expect(submit).toHaveBeenCalledWith({
      customerReturnId: pendingReturn.customerReturnId,
      expectedVersion: "1",
      action: "approve",
      note: null,
    });
  });

  it("retries an unknown request through the same frozen controller instead of creating a new attempt", () => {
    const retrySameAttempt = vi.fn();
    const submit = vi.fn();
    const controller: CustomerReturnRequestController = {
      state: { type: "unknown", binding, idempotencyKey: "stable-key" },
      submit,
      retrySameAttempt,
      abandonUnknownAttempt: vi.fn(),
    };

    render(<CustomerReturnPanel
      {...baseProps()}
      visibility={{ canRead: true, canRequest: true, canApprove: false, canReject: false }}
      requestController={controller}
    />);
    fireEvent.click(screen.getByRole("button", { name: /aynı işlemi yeniden dene/i }));

    expect(retrySameAttempt).toHaveBeenCalledTimes(1);
    expect(submit).not.toHaveBeenCalled();
  });

  it.each(["sending", "unknown", "succeeded"] as const)(
    "disables new decision attempts while the current decision is %s",
    (type) => {
      const submit = vi.fn();
      const state = type === "sending"
        ? { type, binding }
        : type === "unknown"
          ? { type, binding, idempotencyKey: "stable-key" }
          : { type, binding, response: { return: pendingReturn, isReplay: false, originalCorrelationId: "opaque" } };
      const controller: CustomerReturnDecisionController = {
        state,
        submit,
        retrySameAttempt: vi.fn(),
        abandonUnknownAttempt: vi.fn(),
      };

      render(<CustomerReturnPanel
        {...baseProps()}
        visibility={{ canRead: true, canRequest: false, canApprove: true, canReject: true }}
        decisionController={controller}
      />);

      expect((screen.getByRole("button", { name: "Onayla" }) as HTMLButtonElement).disabled).toBe(true);
      expect((screen.getByRole("button", { name: "Reddet" }) as HTMLButtonElement).disabled).toBe(true);
      fireEvent.click(screen.getByRole("button", { name: "Onayla" }));
      expect(submit).not.toHaveBeenCalled();
    },
  );

  it("allows a second partial request only after the settled controller returns to idle", () => {
    const submit = vi.fn();
    const controller = (state: CustomerReturnRequestController["state"]): CustomerReturnRequestController => ({
      state,
      submit,
      retrySameAttempt: vi.fn(),
      abandonUnknownAttempt: vi.fn(),
    });
    const view = render(<CustomerReturnPanel
      {...baseProps()}
      visibility={{ canRead: true, canRequest: true, canApprove: false, canReject: false }}
      requestController={controller({ type: "idle" })}
    />);
    fireEvent.change(screen.getByLabelText("İade miktarı"), { target: { value: "0.5" } });
    fireEvent.change(screen.getByLabelText("İade nedeni"), { target: { value: "İlk kısmi iade" } });
    fireEvent.change(screen.getByLabelText("İade zamanı"), { target: { value: "2026-10-04T09:00" } });
    fireEvent.click(screen.getByRole("button", { name: "Talep oluştur" }));
    expect(submit).toHaveBeenCalledTimes(1);

    view.rerender(<CustomerReturnPanel
      {...baseProps()}
      visibility={{ canRead: true, canRequest: true, canApprove: false, canReject: false }}
      requestController={controller({
        type: "succeeded",
        binding,
        response: { return: pendingReturn, isReplay: false, originalCorrelationId: "first" },
      })}
    />);
    expect((screen.getByRole("button", { name: "Talep oluştur" }) as HTMLButtonElement).disabled).toBe(true);

    view.rerender(<CustomerReturnPanel
      {...baseProps()}
      visibility={{ canRead: true, canRequest: true, canApprove: false, canReject: false }}
      requestController={controller({ type: "idle" })}
    />);
    fireEvent.change(screen.getByLabelText("İade miktarı"), { target: { value: "0.25" } });
    fireEvent.change(screen.getByLabelText("İade nedeni"), { target: { value: "İkinci kısmi iade" } });
    fireEvent.click(screen.getByRole("button", { name: "Talep oluştur" }));
    expect(submit).toHaveBeenCalledTimes(2);
    expect(submit.mock.calls.map(([draft]) => draft.quantity)).toEqual(["0.5", "0.25"]);
  });

  it("suppresses data and actions after a company or authority binding change", () => {
    render(<CustomerReturnPanel
      {...baseProps()}
      contextBinding="company-b:authority-1:instruction-1"
      visibility={{ canRead: true, canRequest: true, canApprove: true, canReject: true }}
    />);

    expect(screen.queryByText("Ambalaj hasarı")).toBeNull();
    expect(screen.queryByRole("button")).toBeNull();
  });
});
