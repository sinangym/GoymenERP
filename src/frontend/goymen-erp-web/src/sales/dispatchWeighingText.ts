export const DISPATCH_WEIGHING_TEXT = {
  heading: 'Kantar farkı', show: 'Kantar farkını göster', hide: 'Kantar farkını gizle', subject: 'irsaliye kantar farkı',
  missing: 'Kantar farkı kaydı henüz yok; fatura bekliyor.', theoretical: 'Teorik kg', scale: 'Kantar kg',
  difference: 'Kg farkı', excess: 'Toplam fazla kg', state: 'Kayıt durumu', decision: 'Karar', band: 'Fark bandı',
  waits: { NotRecorded: 'Kantar farkı kaydı bekleniyor.', PendingApproval: 'Kantar farkı onayı bekleniyor.',
    KgTransferPending: 'Faturaya aktarılacak satır kilogramları bekleniyor.' },
  proceed: 'Kantar farkı yönünden fatura beklemiyor.',
  states: { Recorded: 'Kaydedildi', PendingApproval: 'Onay bekliyor', Approved: 'Onaylandı' },
  decisions: { NoExcess: 'Fazla yok', WithinRate: 'Oran içinde', ApprovalRequired: 'Onay gerekli' },
  bands: { NotEvaluated: 'Değerlendirilmedi', None: 'Yok', Warning: 'Uyarı', Approval: 'Onay', Block: 'Blok' },
} as const
