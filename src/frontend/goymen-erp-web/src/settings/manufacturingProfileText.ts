import type { ManufacturingProcess, ManufacturingProfile } from './manufacturingProfileContract'
export const MANUFACTURING_PROFILE_TEXT = {
  parametersTitle: 'Üretim parametre profilleri', tolerancesTitle: 'Üretim tolerans profilleri', subject: 'üretim profilleri',
  intro: 'Profili hazırlayın ve yetkiniz varsa yayımlayın. Hazırlayan kişi yayımlayabilir. Geçmiş belge anlık görüntüleri değişmez.',
  catalog: 'Onaylı başlangıç kataloğu', catalogNote: 'Başlangıç değerleri salt okunurdur. Sürüm 0 aktarımı bu ekranın kapsamı dışındadır.',
  empty: 'Bu şirkette kayıtlı profil yok.', prepare: 'Yeni profil hazırla', publish: 'Yayımla', process: 'Proses', workCenter: 'İş merkezi',
  allLines: 'Proses geneli', from: 'Başlangıç tarihi', through: 'Bitiş tarihi (boş: süresiz)', reason: 'Gerekçe', values: 'Parametre değerleri',
  omit: 'Boş bırakılan parametre gönderilmez; eksik değer yerine varsayım yapılmaz.', fire: 'Azami fire (%)', mass: 'Kütle dengesi farkı toleransı (%)',
  save: 'Hazırla', cancel: 'Vazgeç', retry: 'Aynı işlemi yeniden dene', pending: 'Kaydediliyor…', unknown: 'İşlemin sonucu bilinmiyor. Aynı istek ve işlem anahtarıyla yeniden deneyin.',
  refused: 'İşlem reddedildi. Yetkiyi, sürümü, değerleri ve tarih aralığını kontrol edin.', conflict: 'Kayıt değişti. Güncel liste yeniden okundu; işlemi güncel sürümle başlatın.',
  prepared: 'Taslak hazırlandı. Yayımlanıncaya kadar kullanılmaz.', published: 'Profil yayımlandı.', replay: 'Önceki işlemin sonucu alındı.',
  version: 'Profil sürümü', status: 'Durum', window: 'Geçerlilik', noEnd: 'Süresiz', scope: 'Kapsam', history: 'Kayıt ve yayım bilgileri', preview: 'Etki önizlemesi',
  prospective: 'Değerler seçilen şirket ve kapsamda ileriye dönük uygulanır. Ekran formül veya tolerans hesaplamaz.', unit: 'Birim', choose: 'Seçin', workCenters: 'iş merkezleri',
} as const
export const manufacturingProcessLabel = (process: ManufacturingProcess) => ({ Slitting: 'Dilme', ColdRolling: 'Soğuk hadde', TubeProfile: 'Boru / profil', Paint: 'Boya', CutToLength: 'Boy kesme' })[process]
export const manufacturingProfileStatus = (status: ManufacturingProfile['status']) => ({ Draft: 'Taslak', Approved: 'Önceki akışta onaylı', Published: 'Yayımlandı', StartingValue: 'Başlangıç değeri' })[status]
const parameterLabels: Readonly<Record<string, string>> = { densityKgPerCubicMetre: 'Yoğunluk', lineSpeedMetresPerMinute: 'Hat hızı', filmMicrons: 'Film kalınlığı', solidsRatio: 'Katı madde oranı', scrapFactor: 'Boya fire katsayısı', simultaneousProfiles: 'Eşzamanlı profil sayısı', kerfMm: 'Testere payı', kerfMode: 'Testere payı yöntemi', headTrimMm: 'Baş kesim', tailTrimMm: 'Son kesim', edgeTrimMm: 'Kenar kaybı', innerDiameterMm: 'İç çap', maximumOuterDiameterMm: 'Azami dış çap', toleranceMetres: 'Metre toleransı' }
export const parameterLabel = (key: string) => parameterLabels[key] ?? key
