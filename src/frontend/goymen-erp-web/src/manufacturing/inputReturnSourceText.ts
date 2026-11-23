export const INPUT_RETURN_SOURCE_TEXT = {
  label: 'İadenin kaynak sarf hareketi',
  choose: 'Kaynak sarf seçilmedi',
  help: 'İade, seçilen sarf hareketinin kayıtlı maliyetine bağlanır. Seçim yoksa yalnız Stok’un ispatladığı tek sarf kullanılabilir; belirsiz maliyet hesaplanmış sayılmaz.',
  unavailable: 'Bu etikette emre ait okunabilir sarf hareketi bulunamadı.',
  truncated: 'Hareket penceresi kısmi; yalnız listelenen gerçek sarflardan seçim yapılabilir.',
  stale: 'Seçilen kaynak sarf bu etiket ve emrin güncel hareketlerinde yok; kaynağı yeniden seçin.',
} as const
