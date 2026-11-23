import type { WorkInboxModuleText } from './textTypes'

/** WP7: the Purchasing kinds added after W1-A (purchase request, supplier return cost exception, service order). */
export const PURCHASING_INBOX_TEXT: WorkInboxModuleText = {
  kinds: {
    'purchasing.request.v1': 'Satın alma talebi onayı',
    'purchasing.supplier-return-cost-exception.v1': 'Tedarikçi iadesi maliyet istisnası',
    'purchasing.service-order.v1': 'Hizmet / fason alış siparişi onayı',
  },
  rules: {
    'purchasing.request-approval-required': 'Gönderilen talep, hazırlayan ve gönderen dışında bir kişinin kararını bekliyor',
    'purchasing.supplier-return-manual-cost': 'Fatura maliyeti okunamadı; elle girilen birim maliyet (TL/kg) istisna onayı bekliyor',
    'purchasing.supplier-return-quantity': 'İade edilecek kg',
    'purchasing.service-order-approval-required': 'Her hizmet siparişi hazırlayandan farklı bir kişinin onayını gerektirir',
    'purchasing.service-order-price-revised': 'Onaylı sipariş fiyat revizyonuyla yeniden taslağa döndü; tekrar onay bekliyor',
    'purchasing.service-order-own-material-subcontract': 'Kendi malzememizle giden fason siparişi',
  },
  roles: {
    'company-account': 'Cari hesap',
  },
  permissions: {
    'purchasing.requests.approve': 'Satın alma talebi kararı',
  },
}
