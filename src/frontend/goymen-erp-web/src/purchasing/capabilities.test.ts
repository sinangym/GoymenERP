import { describe, expect, it } from 'vitest'
import { PURCHASING_CAPABILITY, PURCHASING_NAV_LABEL, PURCHASING_ROUTE, purchaseOrderDetailPath, purchaseRequestDetailPath } from './capabilities'

describe('purchasing capability contract', () => {
  it('pins the owner ids and paths without publishing a create navigation entry', () => {
    expect(PURCHASING_CAPABILITY).toEqual({
      additionalCostRead: 'purchasing.additional-costs.read',
      additionalCostRecord: 'purchasing.additional-costs.record',
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
      orderCancel: 'purchasing.orders.cancel',
      orderLineScheduleWrite: 'purchasing.orders.line-schedule.write',
      orderSupplierRevise: 'purchasing.orders.supplier-revise',
    })
    expect(PURCHASING_ROUTE[PURCHASING_CAPABILITY.requestList]).toBe('/satinalma/talepler')
    expect(PURCHASING_ROUTE[PURCHASING_CAPABILITY.additionalCostRead]).toBe('/satinalma/ek-masraflar/:sourceKind/:orderId')
    expect(PURCHASING_ROUTE[PURCHASING_CAPABILITY.requestCreate]).toBe('/satinalma/talepler/yeni')
    expect(PURCHASING_ROUTE[PURCHASING_CAPABILITY.requestDetail]).toBe('/satinalma/talepler/:requestId')
    expect(PURCHASING_NAV_LABEL[PURCHASING_CAPABILITY.requestList]).toBe('Satın alma talepleri')
    expect(PURCHASING_NAV_LABEL[PURCHASING_CAPABILITY.requestCreate]).toBe('')
    expect(purchaseRequestDetailPath('a/b')).toBe('/satinalma/talepler/a%2Fb')
    expect(PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderList]).toBe('/satinalma/siparisler')
    expect(PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderCreate]).toBe('/satinalma/siparisler/yeni')
    expect(PURCHASING_NAV_LABEL[PURCHASING_CAPABILITY.orderCreate]).toBe('')
    expect(purchaseOrderDetailPath('a/b')).toBe('/satinalma/siparisler/a%2Fb')
  })
})
