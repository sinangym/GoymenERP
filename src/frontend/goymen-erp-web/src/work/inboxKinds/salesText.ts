/**
 * WP4 — the Turkish words of the remaining Sales approval kinds in the Work inbox (request, invoice, subcontract service
 * invoice, customer return, contract discount): kind names, the owner rule codes of the explanation card, the reference roles
 * and the owner approve permissions ("kimde bekliyor"). Kept in the Sales file so module packages add their words without
 * touching each other; `workInboxText.ts` merges these maps into its own.
 */

export const SALES_INBOX_KIND_TEXT: Readonly<Record<string, string>> = {
  'sales.request.v1': 'Satış talebi onayı',
  'sales.invoice.v1': 'Satış faturası onayı',
  'sales.subcontract-invoice.v1': 'Fason hizmet faturası onayı',
  'sales.customer-return.v1': 'Müşteri iadesi onayı',
  'sales.contract-discount.v1': 'Sözleşme iskontosu onayı',
  'sales.quote-conversion-request.v1': 'Süresi dolmuş teklif dönüşüm talebi',
  'sales.over-delivery-request.v1': 'Fazla teslim onay talebi',
}

export const SALES_INBOX_RULE_TEXT: Readonly<Record<string, string>> = {
  'sales.request-approval-required': 'Her satış talebi kaydedildiği anda onay bekler',
  'sales.invoice-approval-required': 'Fatura onaya gönderildi; hazırlayan ve gönderen dışında bir kişinin onayını bekliyor',
  'sales.subcontract-invoice-approval-required':
    'Fason hizmet faturası onaya gönderildi; hazırlayan ve gönderen dışında bir kişinin onayını bekliyor',
  'sales.customer-return-approval-required':
    'Müşteri iade faturası onaya gönderildi; hazırlayan ve gönderen dışında bir kişinin onayını bekliyor',
  'sales.contract-discount-over-limit': 'Sözleşme iskontosu kayıttaki sınırı aşıyor (sınır / iskonto oranı / fark)',
  'sales.contract-discount-limit-undefined': 'Kayıt anında iskonto sınırı tanımlı değildi; her iskonto onaya düşer',
  'sales.quote-expired-conversion-requested': 'Satışçı süresi dolmuş teklifin siparişe dönüştürülmesi için onay istedi',
  'sales.over-delivery-requested': 'Sevk, sipariş satırını tolerans onay bandına taşıyor (sipariş / talep edilen sevk toplamı / fazla)',
}

export const SALES_INBOX_ROLE_TEXT: Readonly<Record<string, string>> = {
  'sales-request': 'Satış talebi',
  'sales-invoice': 'Satış faturası',
  'sales-subcontract-invoice': 'Fason hizmet faturası',
  'sales-customer-return': 'Müşteri iadesi',
  'sales-contract': 'Satış sözleşmesi',
  'sales-quote': 'Satış teklifi',
}

export const SALES_INBOX_PERMISSION_TEXT: Readonly<Record<string, string>> = {
  'sales.requests.approve': 'Satış talebi onayı',
  'sales.invoices.approve': 'Satış faturası onayı',
  'sales.returns.approve': 'Müşteri iadesi onayı',
  'sales.contracts.discount.approve': 'Sözleşme iskontosu onayı',
  'sales.quotes.expired-conversion.approve': 'Süresi dolmuş teklif dönüşüm onayı',
  'sales.orders.over-delivery.approve': 'Fazla teslim onayı',
}
