export const PAINT_SELECTION_TEXT = {
  heading: 'Boya seçimi', product: 'Kullanılacak boya ürünü', supplier: 'Boya tedarikçisi', color: 'Renk (isteğe bağlı)',
  note: 'Renk notu (isteğe bağlı)', choose: 'Seçin', none: 'Renk seçilmedi', search: 'Tedarikçi ara', loading: 'Boya seçim kaynakları yükleniyor.',
  emptyProducts: 'Seçilebilir boya ürünü yok. Ürün boya üyelik kaynağını kontrol edin.', emptySuppliers: 'Seçilebilir tedarikçi bulunamadı.',
  more: 'Tedarikçi sonuçları sınırlı; aramayı daraltın.', required: 'Boya ürünü ve tedarikçisi kaynak listesinden seçilmelidir.',
  unavailable: 'Boya seçim kaynakları şu anda okunamıyor. Yeniden deneyin.', denied: 'Boya seçim kaynaklarına erişim yetkiniz yok.',
  missing: 'Boya seçim kaynağı bulunamadı.', malformed: 'Boya seçim kaynağının yanıtı okunamadı.', offline: 'Çevrimdışısınız. Bağlantıyı kontrol edin.',
  stale: 'Kayıtlı boya seçimi güncel kaynak listesinde yok veya sürümü değişti. Seçimi gözden geçirin.', retry: 'Yeniden yükle',
  productVersion: 'ürün sürümü', membershipVersion: 'boya üyelik sürümü', sourceVersions: 'kaynak sürümü',
} as const

export function paintSelectionFailureText(code: string | null): string | null {
  switch (code) {
    case 'manufacturing_paint_selection_required': return PAINT_SELECTION_TEXT.required
    case 'manufacturing_paint_selection_ineligible': return 'Seçilen boya ürünü, tedarikçi veya renk artık kullanım için uygun değil. Kaynak listesinden yeniden seçin.'
    case 'manufacturing_paint_selection_version_conflict': return PAINT_SELECTION_TEXT.stale
    case 'manufacturing_paint_selection_unavailable': return PAINT_SELECTION_TEXT.unavailable
    default: return null
  }
}
