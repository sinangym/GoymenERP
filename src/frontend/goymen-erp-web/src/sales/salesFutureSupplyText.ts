export const SALES_FUTURE_SUPPLY_TEXT = {
  show: 'Alıştan tedariki incele', hide: 'Alıştan tedariki kapat',
  choose: 'Açık alış satırından ayır / ayırmayı yönet',
  explanation: 'Gelecek tedarik fiziksel stok değildir. Kaynak planı miktarı kaydetmek, alıştan ayırma oluşturmaz. Ayırma kendi yetkisiyle ayrıca kaydedilir.',
  warning: 'Kaynak uyarısı', otherRelease: 'Ayırma bırakıldı',
  reasons: {
    'manual-release': 'Ayırma elle bırakıldı',
    'purchasing-link-dropped': 'Kaynak bağı düşürüldü',
    'source-cancelled': 'Kaynak iptal edildi',
    'purchase-line-closed': 'Kaynak eksik teslimle kapandı',
    'acceptance-link-failed': 'Kabulde bağlanamadı',
    'acceptance-reversed-no-supply': 'Kabul geri alındı, kaynak yok',
    'sales-line-cancelled': 'Satış satırı iptal edildi',
    'sales-line-reduced': 'Satış satırının miktarı azaltıldı',
  } as Readonly<Record<string, string>>,
} as const
