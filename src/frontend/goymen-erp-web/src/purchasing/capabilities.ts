export const PURCHASING_CAPABILITY = {
  orderList: 'purchasing.orders.list',
  orderCreate: 'purchasing.orders.create',
  orderDetail: 'purchasing.orders.detail',
  orderApprove: 'purchasing.orders.approve',
} as const

export type PurchasingCapabilityId =
  (typeof PURCHASING_CAPABILITY)[keyof typeof PURCHASING_CAPABILITY]

export const PURCHASING_ROUTE: Record<string, string> = {
  [PURCHASING_CAPABILITY.orderList]: '/satinalma/siparisler',
  [PURCHASING_CAPABILITY.orderCreate]: '/satinalma/siparisler/yeni',
  [PURCHASING_CAPABILITY.orderDetail]: '/satinalma/siparisler/:orderId',
}

export const PURCHASING_NAV_LABEL: Record<string, string> = {
  [PURCHASING_CAPABILITY.orderList]: 'Satın alma siparişleri',
  [PURCHASING_CAPABILITY.orderCreate]: '',
  [PURCHASING_CAPABILITY.orderDetail]: '',
}

export function purchaseOrderDetailPath(orderId: string): string {
  return `/satinalma/siparisler/${encodeURIComponent(orderId)}`
}
