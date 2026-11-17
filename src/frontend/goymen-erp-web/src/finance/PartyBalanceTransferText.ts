export const BALANCE_TRANSFER_TEXT = {
  heading: 'Cari bakiye devri', predecessor: 'Eski cari', successor: 'Yeni cari', search: 'Cari ara',
  choose: 'Seçin', previous: 'Önceki sayfa', next: 'Sonraki sayfa', preview: 'Tam bakiyeyi getir',
  occurredOn: 'Devir tarihi', reason: 'Devir gerekçesi', evidence: 'Hukuki dayanak belgesi',
  documents: 'Eski carinin dayanak belgeleri', submit: 'Bakiyeyi devret', retry: 'Aynı anahtarla yeniden dene',
  refresh: 'Güncel bilgileri getir', empty: 'Devredilecek açık tutar yok.',
  explanation: 'Her para biriminin tam bakiyesi tek fişle devredilir. Eski belgeler değişmez. Sonradan oluşan fark eski caride kalır; ayrıca devir gerekir.',
  unknown: 'Gönderimin sonucu bilinmiyor. Aynı anahtar ve içerik korunuyor; yeni bir devir başlatılamaz.',
  success: 'Bakiye devri kaydedildi.', refused: 'Devir kaydedilmedi. Güncel önizlemeyi alıp dayanak belgeyi ve tarihi kontrol edin.',
  conflict: 'Bakiye veya cari bağı değişmiş. Güncel önizlemeyi yeniden alın.',
  currency: 'Para birimi', balance: 'Bakiye', receivable: 'Açık alacak', payable: 'Açık borç',
  noDocument: 'Temiz ve indirilebilir dayanak belge seçilmelidir.',
  sides: { Zero: 'Sıfır', Debit: 'Borç', Credit: 'Alacak' },
} as const
