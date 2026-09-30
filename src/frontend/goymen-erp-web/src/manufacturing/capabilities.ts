/**
 * Capability ids of the Manufacturing order screens — the frontend's side of the
 * coordinator-approved MFG-UI card (D2), not a grant.
 *
 * The server's `ManufacturingShellCapabilityProvider` declares these ids PUBLISHED
 * (`ImplementationAvailable: true`, MFG-UI-F #349, after the `mfg` browser acceptance). The
 * registry still publishes each only to an actor holding its permissions in the context
 * company; for anyone else `CapabilityRoute` closes the route onto the neutral not-found
 * block and `hasCapability` answers false. The frontend never mints a capability to open
 * its own screen, and the server re-authorizes every request regardless (ERP-000K §10.2).
 *
 * WHY TEN IDS. They map onto the permissions the server enforces separately
 * (`ManufacturingEndpoints`):
 *
 *   manufacturing.orders.detail <- manufacturing.orders.read
 *   manufacturing.orders.facts  <- manufacturing.orders.read + manufacturing.facts.record
 *   manufacturing.orders.list   <- manufacturing.orders.read
 *   manufacturing.orders.close  <- manufacturing.orders.read + manufacturing.orders.close
 *   manufacturing.plans.create  <- manufacturing.plans.write
 *   manufacturing.plans.detail  <- manufacturing.plans.read
 *   manufacturing.plans.approve <- manufacturing.plans.read + manufacturing.plans.approve
 *   manufacturing.plans.order-create <- manufacturing.plans.read + manufacturing.orders.create
 *   manufacturing.plans.edit    <- manufacturing.plans.read + manufacturing.plans.write (MFG-PLAN-EDIT: the plan save's grant)
 *   manufacturing.plans.list    <- manufacturing.plans.read (MFG-PLAN-LIST: the plan read's own grant)
 *
 * Reading an order and recording a fact on it are different authorities, so a reader
 * sees the balances and NO form (card D3: without the capability the control is not
 * drawn; the server enforces either way). Closing is a third authority: whoever may
 * record a fact does not thereby close the order, and the close button is a control of
 * the detail screen, drawn only for its own capability. Plan approval, order creation and the
 * Draft edit follow the same child-control rule on plan detail.
 *
 * This is a technical, functional sub-view (card D1). The canonical production screens
 * (PR-09/13/14/15) are `DECISION_ONLY_REFERENCE_MISSING`; nothing here is a locked
 * visual reference or the Master §7.7 realization modes.
 */

export const MANUFACTURING_CAPABILITY = {
  /** One production order: its roll balances. Route `/uretim/emir/:orderId`. */
  orderDetail: 'manufacturing.orders.detail',
  /** The fact forms INSIDE the order screen. Not a route — a control. */
  orderFacts: 'manufacturing.orders.facts',
  /** MFG-02 the company's production orders. Route `/uretim/emirler`. */
  orderList: 'manufacturing.orders.list',
  /** The close action INSIDE the order screen. Not a route — a control, like the fact forms. */
  orderClose: 'manufacturing.orders.close',
  /** Create a Draft plan from authorized work-center and Inventory candidate reads. */
  planCreate: 'manufacturing.plans.create',
  /** Reopen one stored plan through `manufacturing.plans.read`. */
  planDetail: 'manufacturing.plans.detail',
  /** Approve the Draft shown on the plan detail screen. Not a route — a control. */
  planApprove: 'manufacturing.plans.approve',
  /** Create one order from the Approved plan shown on the detail screen. Not a route — a control. */
  planOrderCreate: 'manufacturing.plans.order-create',
  /** MFG-PLAN-EDIT: edit the Draft shown on the detail screen, with its history. Not a route — a control. */
  planEdit: 'manufacturing.plans.edit',
  /** MFG-PLAN-LIST the company's saved plans, so a closed plan is found and reopened. Route `/uretim/planlar`. */
  planList: 'manufacturing.plans.list',
} as const

export type ManufacturingCapabilityId =
  (typeof MANUFACTURING_CAPABILITY)[keyof typeof MANUFACTURING_CAPABILITY]

/** Route path per routed capability, as handed to the shell owner. */
export const MANUFACTURING_ROUTE: Record<string, string> = {
  [MANUFACTURING_CAPABILITY.planCreate]: '/uretim/plan/yeni',
  [MANUFACTURING_CAPABILITY.planDetail]: '/uretim/plan/:planId',
  [MANUFACTURING_CAPABILITY.orderDetail]: '/uretim/emir/:orderId',
  [MANUFACTURING_CAPABILITY.orderList]: '/uretim/emirler',
  [MANUFACTURING_CAPABILITY.planList]: '/uretim/planlar',
}

/**
 * Nav label per routed capability. The detail has none — an order is opened from the list or by
 * its id — and the order list is the module's one sidebar entry. The plan list has none either: it
 * is reached from the order list and from the plan screens, so the sidebar keeps its one entry.
 */
export const MANUFACTURING_NAV_LABEL: Record<string, string> = {
  [MANUFACTURING_CAPABILITY.orderDetail]: '',
  [MANUFACTURING_CAPABILITY.orderList]: 'Üretim emirleri',
  [MANUFACTURING_CAPABILITY.planCreate]: '',
  [MANUFACTURING_CAPABILITY.planDetail]: '',
  [MANUFACTURING_CAPABILITY.planList]: '',
}

export function planCreatePath(): string {
  return MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.planCreate]
}

/**
 * AT-063 "Üretim planla" from an ST-01 stock row: the plan-create URL names the row's label (`etiket`, its id) and
 * its code (`kod`, only the search hint for Inventory's candidate read). Neither is trusted: the page selects the
 * roll only when the candidate read answers that exact label id as draft-selectable, and the save sends the
 * candidate's label version for the server to revalidate exactly as a manual pick.
 */
export const PLAN_CREATE_LABEL_PARAM = 'etiket' as const
export const PLAN_CREATE_LABEL_CODE_PARAM = 'kod' as const
/** Inventory's label code bound (MaterialLabel.MaximumCodeLength); a longer hint is not carried. */
export const PLAN_CREATE_LABEL_CODE_MAX_LENGTH = 64

export function planCreateFromLabelPath(labelId: string, labelCode: string): string {
  const query = new URLSearchParams({ [PLAN_CREATE_LABEL_PARAM]: labelId })
  const code = labelCode.trim()
  if (code !== '' && code.length <= PLAN_CREATE_LABEL_CODE_MAX_LENGTH) query.set(PLAN_CREATE_LABEL_CODE_PARAM, code)
  return `${planCreatePath()}?${query.toString()}`
}

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/

/** The label a plan-create URL preselects, or null when the URL names none or a malformed one. */
export function parsePlanCreateLabel(search: string): { readonly labelId: string; readonly labelCode: string } | null {
  const params = new URLSearchParams(search)
  const labelId = params.get(PLAN_CREATE_LABEL_PARAM)?.trim() ?? ''
  if (!GUID.test(labelId)) return null
  const code = params.get(PLAN_CREATE_LABEL_CODE_PARAM)?.trim() ?? ''
  return { labelId, labelCode: code.length <= PLAN_CREATE_LABEL_CODE_MAX_LENGTH ? code : '' }
}

export function planDetailPath(planId: string): string {
  return `/uretim/plan/${encodeURIComponent(planId)}`
}

/** Concrete path to one order. */
export function orderDetailPath(orderId: string): string {
  return `/uretim/emir/${encodeURIComponent(orderId)}`
}

/** The plan list's own path. */
export function planListPath(): string {
  return MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.planList]
}

/** The order list's own path. */
export function orderListPath(): string {
  return MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.orderList]
}
