export const PURCHASING_CAPABILITY = {
  requestList: 'purchasing.requests.list',
  requestCreate: 'purchasing.requests.create',
  requestDetail: 'purchasing.requests.detail',
  requestSubmit: 'purchasing.requests.submit',
  orderList: 'purchasing.orders.list',
  orderCreate: 'purchasing.orders.create',
  orderDetail: 'purchasing.orders.detail',
  orderApprove: 'purchasing.orders.approve',
} as const

export type PurchasingCapabilityId =
  (typeof PURCHASING_CAPABILITY)[keyof typeof PURCHASING_CAPABILITY]

export const PURCHASING_ROUTE: Record<string, string> = {
  [PURCHASING_CAPABILITY.requestList]: '/satinalma/talepler',
  [PURCHASING_CAPABILITY.requestCreate]: '/satinalma/talepler/yeni',
  [PURCHASING_CAPABILITY.requestDetail]: '/satinalma/talepler/:requestId',
  [PURCHASING_CAPABILITY.orderList]: '/satinalma/siparisler',
  [PURCHASING_CAPABILITY.orderCreate]: '/satinalma/siparisler/yeni',
  [PURCHASING_CAPABILITY.orderDetail]: '/satinalma/siparisler/:orderId',
}

export const PURCHASING_NAV_LABEL: Record<string, string> = {
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
