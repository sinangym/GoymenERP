export const TRANSFER_TEXT = {
  title: 'Depolar arası transferler', subject: 'transferler', detail: 'Transfer satırları',
  from: 'Kaynak depo', to: 'Hedef depo', plannedAt: 'Plan zamanı', status: 'Durum',
  inspect: 'Satırları göster', label: 'Etiket', line: 'Satır', plannedKg: 'Planlanan kg', shippedKg: 'Sevk edilen kg',
  differenceKg: 'Sevk farkı kg', unknown: 'Kaydedilmemiş', window: 'En son 25 transfer gösteriliyor. Daha eski kayıtlar bu pencerede yer almıyor.',
  openLabel: 'Etiket ayrıntısını aç', back: 'Stok pozisyonlarına dön', entryLink: 'Depolar arası transferleri aç',
  statuses: { Planned: 'Planlandı', InTransit: 'Yolda', PartiallyAccepted: 'Kısmen teslim alındı', Accepted: 'Teslim alındı',
    DispatchCancelled: 'Sevk iptal edildi', WrittenOff: 'Kayıp kaydedildi', Closed: 'Kapandı' } as Record<string, string>,
} as const
