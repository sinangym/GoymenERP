export const PURCHASING_CAPABILITY = {
  serviceOrderList: 'purchasing.service-orders.list',
  serviceOrderCreate: 'purchasing.service-orders.create',
  serviceOrderDetail: 'purchasing.service-orders.detail',
  serviceOrderApprove: 'purchasing.service-orders.approve',
  serviceOrderAccept: 'purchasing.service-orders.accept',
  serviceOrderRevisePrices: 'purchasing.service-orders.prices-revise',
  invoiceList: 'purchasing.invoices.list',
  invoiceImport: 'purchasing.invoices.import',
  invoiceDetail: 'purchasing.invoices.detail',
  invoiceMatch: 'purchasing.invoices.match',
  invoiceApproveVariance: 'purchasing.invoices.approve-variance',
  invoiceFinalize: 'purchasing.invoices.finalize',
  requestList: 'purchasing.requests.list',
  requestCreate: 'purchasing.requests.create',
  requestDetail: 'purchasing.requests.detail',
  requestSubmit: 'purchasing.requests.submit',
  requestApprove: 'purchasing.requests.approve',
  requestWithdraw: 'purchasing.requests.withdraw',
  orderList: 'purchasing.orders.list',
  orderCreate: 'purchasing.orders.create',
  orderDetail: 'purchasing.orders.detail',
  orderApprove: 'purchasing.orders.approve',
  supplierReturnRecord: 'purchasing.supplier-returns.record',
  /** A1-3 (plan API-10/UI-05): the order remainder cancellation, a child control of the order detail. */
  orderCancel: 'purchasing.orders.cancel',
  /**
   * F-027 (UI-07, EK16 §2a; ALS-D94): the line termin change of an order, a child control of the order detail measured with
   * `purchasing.orders.revise` (name registered only; the grant is a human gate).
   */
  orderLineScheduleWrite: 'purchasing.orders.line-schedule.write',
  orderSupplierRevise: 'purchasing.orders.supplier-revise',
} as const

export type PurchasingCapabilityId =
  (typeof PURCHASING_CAPABILITY)[keyof typeof PURCHASING_CAPABILITY]

export const PURCHASING_ROUTE: Record<string, string> = {
  [PURCHASING_CAPABILITY.serviceOrderList]: '/satinalma/hizmet-siparisleri',
  [PURCHASING_CAPABILITY.serviceOrderCreate]: '/satinalma/hizmet-siparisleri/yeni',
  [PURCHASING_CAPABILITY.serviceOrderDetail]: '/satinalma/hizmet-siparisleri/:serviceOrderId',
  [PURCHASING_CAPABILITY.invoiceList]: '/satinalma/faturalar',
  [PURCHASING_CAPABILITY.invoiceDetail]: '/satinalma/faturalar/:invoiceId',
  [PURCHASING_CAPABILITY.requestList]: '/satinalma/talepler',
  [PURCHASING_CAPABILITY.requestCreate]: '/satinalma/talepler/yeni',
  [PURCHASING_CAPABILITY.requestDetail]: '/satinalma/talepler/:requestId',
  [PURCHASING_CAPABILITY.orderList]: '/satinalma/siparisler',
  [PURCHASING_CAPABILITY.orderCreate]: '/satinalma/siparisler/yeni',
  [PURCHASING_CAPABILITY.orderDetail]: '/satinalma/siparisler/:orderId',
}

export const PURCHASING_NAV_LABEL: Record<string, string> = {
  [PURCHASING_CAPABILITY.serviceOrderList]: 'Hizmet ve giden fason alış siparişleri',
  [PURCHASING_CAPABILITY.serviceOrderCreate]: '',
  [PURCHASING_CAPABILITY.serviceOrderDetail]: '',
  [PURCHASING_CAPABILITY.invoiceList]: 'Alış faturaları',
  [PURCHASING_CAPABILITY.invoiceDetail]: '',
  [PURCHASING_CAPABILITY.requestList]: 'Satın alma talepleri',
  [PURCHASING_CAPABILITY.requestCreate]: '',
  [PURCHASING_CAPABILITY.requestDetail]: '',
  [PURCHASING_CAPABILITY.orderList]: 'Satın alma siparişleri',
  [PURCHASING_CAPABILITY.orderCreate]: '',
  [PURCHASING_CAPABILITY.orderDetail]: '',
}

export function purchaseOrderDetailPath(orderId: string): string {
  return `/satinalma/siparisler/${encodeURIComponent(orderId)}`
}

export function purchaseRequestDetailPath(requestId: string): string {
  return `/satinalma/talepler/${encodeURIComponent(requestId)}`
}
