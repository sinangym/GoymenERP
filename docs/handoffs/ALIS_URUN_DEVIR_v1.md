# Alış → Ürün görev notu v1

Tarih: 2026-11-02 · Yazan: Alış şeridi (Claude) · Alıcı: Ürün modülünü yürüten konuşma
Karar: `docs/decisions/SINAN_KARARLARI_2026-11-02_ALIS_EK7.md` ALS-D51 (çevrim ürün kartından; fiyat birimi ürün
kartından varsayılan, siparişte değiştirilebilir). Alış şeridi Ürün dosyalarını değiştirmez. Bu not iş tanımıdır;
Ürün'ün kendi karar, review ve merge kapıları geçerlidir. Arayüz adları öneridir.

## U-A1 · kg ↔ adet ↔ metre çevrimi ve varsayılan fiyat birimi (ALS-D51, DEC-PRD-006)

- **Bugün:** Ürün modülü çevrim varsaymıyor (README: "There is no assumed conversion"; U-08 çevrim politikası
  kabul edilmedi). Alış satırı yalnız Ayarlar profiliyle doğrulanan tek birimle çalışıyor (`PurchaseUnitVerifier`).
- **İstenen (Ürün):**
  1. Ürün kartında, ürün özelliğine göre kg / adet / metre arasındaki çevrim (DEC-PRD-006'daki sabit, formül bağlı
     veya fiilî sınıf; fiilî çevrimde katsayı ölçülen değerden gelir, ör. kantar kg'ı ile sayılan adet); çevrimi olmayan ürün açıkça "çevrim yok" döner, varsayılan katsayı üretilmez.
  2. Ürün kartında alış için **varsayılan fiyat birimi** (birim fiyatın çarpıldığı miktar).
  3. Bir okuma portu (öneri: `IProductPurchaseUnitConversionV1`): şirket + ürün → desteklenen birimler, çevrim
     sınıfı/sürümü, varsayılan fiyat birimi. Hesap ortak deterministic calculator'da yapılır.
- **Alış tarafı:** A2a fiyat modeli satırda giriş birimi + fiyat birimi taşır; port yokken satır yalnız girilen
  birimle kaydedilir, türetilen miktarlar "hesaplanamıyor" kalır (fail-closed). A3 kabulü ve A5 üçlü kontrol aynı
  portu okur.
