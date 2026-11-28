export const SALES_CONTRACT_DISCOUNT_WORK_TEXT = {
  title: 'Satış sözleşmesi iskonto karar bilgileri',
  readOnly: 'Kayıtlı sözleşmenin tamamı ve sunucunun karar uygunluğu gösterilir. Fiyatlar yalnız mevcut fiyat okuma yetkisiyle görünür.',
  retained: 'Önceki sözleşme kararınız özgün anahtarı, sürümü ve hazırlanmış gerekçesiyle doğrulanıyor. Güncel sözleşme bu isteğin yerine kullanılmıyor.',
  canonicalReason: 'Ret gerekçesi onaydan önce başındaki ve sonundaki boşluklardan arındırılır; aynı gerekçe karar isteğinde korunur.',
  none: 'Yok', yes: 'Evet', no: 'Hayır', withheld: 'Fiyat okuma yetkisiyle sınırlı',
  installments: 'Kayıtlı taksit planı', rules: 'Kayıtlı iskonto kuralları',
  states: {PendingDiscountApproval: 'İskonto onayı bekliyor', Active: 'Etkin', DiscountRejected: 'İskonto reddedildi'},
  kinds: {Line: 'Satır', Total: 'Toplam'},
  fields: {companyId: 'Şirket kimliği', id: 'Sözleşme kimliği', contractNo: 'Sözleşme no', partyId: 'Cari kimliği', companyAccountId: 'Şirket cari hesabı', currencyId: 'Para birimi kimliği', moneyScale: 'Kayıtlı para ölçeği', state: 'Sözleşme durumu', version: 'Sözleşme sürümü', validFrom: 'Geçerlilik başlangıcı', validThrough: 'Geçerlilik bitişi', amountDisclosure: 'Fiyat görünürlüğü', plannedTotalAmount: 'Kayıtlı planlanan toplam', clauses: 'Sözleşme maddeleri', discountApprovalRequired: 'İskonto onayı gerekli', discountLimitDefined: 'Kayıtlı iskonto sınırı tanımlı', discountLimitPercent: 'Kayıtlı iskonto sınırı (%)', isInEffectToday: 'Sunucuya göre bugün yürürlükte', createdBy: 'Oluşturan', createdAtUtc: 'Oluşturma zamanı', decidedBy: 'Karar veren', decidedAtUtc: 'Karar zamanı', rejectionReason: 'Kayıtlı ret gerekçesi', approvalSupported: 'Onay verilebilir', rejectionSupported: 'Reddedilebilir', installmentNo: 'Taksit no', dueDate: 'Vade', amount: 'Kayıtlı tutar', ruleNo: 'Kural no', kind: 'İskonto türü', label: 'Kural açıklaması', ratePercent: 'Kayıtlı oran (%)'},
} as const
