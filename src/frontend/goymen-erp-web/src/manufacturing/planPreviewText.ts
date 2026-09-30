/**
 * Display names for the plan preview. Labels only: every name is the server's own parameter or value name, shown with
 * its Turkish label; an unknown name is shown as it came, never dropped. Nothing here is a value, a default or a
 * formula.
 */

const PARAMETER_LABELS: Readonly<Record<string, string>> = {
  densityKgPerCubicMetre: 'Yoğunluk (kg/m³)',
  lineSpeedMetresPerMinute: 'Hat hızı (m/dk)',
  netUsableWidthMm: 'Net kullanılabilir en (mm)',
  parentWidthMm: 'Ana rulo eni (mm)',
  thicknessMm: 'Kalınlık (mm)',
  widthMm: 'En (mm)',
  inputThicknessMm: 'Giriş kalınlığı (mm)',
  targetThicknessMm: 'Hedef kalınlık (mm)',
  scrapKg: 'Hurda (kg)',
  unitKg: 'Birim kg (ürün/paket masterı)',
  packageCount: 'Paket adedi',
  piecesPerPackage: 'Paketteki adet',
  pieceLengthMm: 'Parça boyu (mm)',
  profileLengthMm: 'Profil boyu (mm)',
  simultaneousProfiles: 'Eşzamanlı profil adedi',
  perimeterMm: 'Profil çevresi (mm)',
  filmMicrons: 'Kuru film kalınlığı (µm)',
  solidsRatio: 'Katı madde oranı',
  scrapFactor: 'Fire katsayısı',
  totalPieces: 'Toplam adet (TUB-001 sonucu)',
  pieces: 'Toplam adet (PNT-001 sonucu)',
  areaSquareMetres: 'Boya alanı (PNT-005 sonucu)',
  theoreticalGoodOutputKg: 'Teorik iyi ürün kg (plan formülü tanımlı değil)',
}

const LINE_PARAMETER = /^lines\[(\d+)\]\.(bandWidthMm|count|targetLengthMm)$/

const LINE_LABELS: Readonly<Record<string, string>> = {
  bandWidthMm: 'dilme satırı bant eni (mm)',
  count: 'satır adedi',
  targetLengthMm: 'kesim satırı hedef boy (mm)',
}

export function parameterLabel(name: string): string {
  const line = LINE_PARAMETER.exec(name)
  if (line !== null) return `${Number(line[1]) + 1}. ${LINE_LABELS[line[2]]}`
  return PARAMETER_LABELS[name] ?? name
}

const VALUE_LABELS: Readonly<Record<string, string>> = {
  kgPerMillimetreWidth: 'mm en başına kg',
  totalUsedWidthMm: 'Kullanılan toplam en (mm)',
  remainingWidthMm: 'Kalan en (mm)',
  planKg: 'Plan kg',
  remainingKg: 'Kalan kg',
  kgPerMetre: 'Metre başına kg',
  walkingMetres: 'Yürüyen metre',
  minutes: 'Süre (dk)',
  inputMetres: 'Giriş metresi',
  outputKg: 'Çıkış kg',
  elongationRatio: 'Uzama oranı',
  outputMetres: 'Çıkış metresi',
  reductionPercent: 'Ezme oranı (%)',
  planMetres: 'Plan metresi',
  remainingMetres: 'Kalan metre',
  totalPieces: 'Toplam adet',
  totalMetres: 'Toplam metre',
  runMinutes: 'Çalışma süresi (dk)',
  totalKg: 'Toplam kg',
  profileMetres: 'Profil metresi',
  linePassMetres: 'Hat geçiş metresi',
  areaSquareMetres: 'Alan (m²)',
  theoreticalLitres: 'Teorik litre',
}

export function valueLabel(name: string): string {
  return VALUE_LABELS[name] ?? name
}

export const OUTCOME_LABELS: Readonly<Record<string, string>> = {
  Computed: 'Hesaplandı',
  Unavailable: 'Parametre mevcut değil',
  Refused: 'Girdi reddedildi',
}

export const PLAN_PREVIEW_TEXT = {
  heading: 'Canlı teorik önizleme',
  note:
    'Önizleme kayıt, rezervasyon veya stok hareketi değildir. Hesap, kaydın kullandığı aynı sunucu hesaplayıcısıyla yapılır; ekranda formül yoktur.',
  waiting: 'Girdi değişti; önizleme hesaplanıyor…',
  current: 'Önizleme güncel: aşağıdaki sonuç formdaki girdilere aittir.',
  stale: 'Güncel değil — aşağıdaki sonuç önceki girdilere aittir.',
  incomplete: 'Önizleme için eksik veya geçersiz alan:',
  missingHeading: 'Parametre mevcut değil',
  missingNote:
    'Bu değerlerin kararı (OPEN-007) veya kaynağı (Ayarlar / Politika Kaydı, ürün ve etiket okuması) henüz yok. Önizleme varsayılan değer kullanmaz; eksik parametreyle hesap yapılmaz.',
  saveNote: 'Önizleme kaydı engellemez; kayıt, girdileri sunucuda yeniden doğrular.',
} as const

export function previewFailure(reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed'): string {
  if (reason === 'offline') return 'Çevrimdışısınız; önizleme hesaplanamadı.'
  if (reason === 'malformed') return 'Önizleme yanıtı beklenen sözleşmeyle okunamadı.'
  if (reason === 'service-unavailable') return 'Önizleme kaynağı şu an yanıt vermiyor (503).'
  return 'Önizleme kaynağına ulaşılamadı.'
}

export function previewRefusal(status: number, code: string | null, field: string | null): string {
  const where = field === null ? '' : ` Alan: ${field}.`
  if (status === 401 || status === 403) return 'Önizleme için plan yazma yetkiniz artık geçerli değil.'
  if (status === 404) return 'Önizlenen ana rulo bu şirket bağlamında bulunamadı.'
  if (status === 409) return `Seçilen etiketin sürümü değişti; ana ruloyu yeniden seçin (${code ?? 'conflict'}).${where}`
  if (status === 400) return `Girdi okunamadı (${code ?? 'request_invalid'}).${where}`
  return `Girdi kabul edilmedi (${code ?? status}).${where}`
}
