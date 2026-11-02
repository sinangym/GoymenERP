# Durum Makineleri Sözleşmesi

Durum geçişleri backend’de tanımlanır; UI renkleri kural değildir.

## Sipariş ayrı eksenleri

- Yaşam döngüsü: Taslak / Aktif / Kapalı / İptal
- İç onay: Hazırlanıyor / Onay bekliyor / Onaylı / Reddedildi
- Kaynak: Kaynaksız / Kısmi / Tam kaynaklı
- Gerçekleşme: Başlamadı / Kısmi / Tamamlandı
- Faturalama: Yok / Kısmi / Tam
- Finansal kapanış: Açık / Kısmi / Kapalı
- Müşteri onayı: Bekleniyor / Alındı / Vazgeçildi

## Satın Alma

Kaynak: kod (`src/backend/Modules/Purchasing/**`), `SINAN_KARARLARI_2026-11-02_ALIS*.md`,
`SINAN_KARARLARI_2026-10-27_NO161_TALEP_RED_SIPARIS_IPTAL.md`, plan `docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md`.
**Mevcut** satırlar kodda çalışır; **Hedef** satırlar karara bağlanmış ama uygulanmamıştır.

**Mevcut (2026-11-02, A1-1 ve A1-1b dahil):**

- Talep: Taslak → Gönderildi → Onaylı / Reddedildi (`PurchaseRequestStatus` Draft / Submitted / Approved / Rejected;
  A1-1). Talep taahhüt değildir. Onay yalnız Gönderildi talepte; ret Gönderildi veya Onaylı talepte, gerekçe zorunlu,
  talebe bağlı Onaylı sipariş varken reddedilmez (No.161 §1). Hazırlayan (oluşturan) ve gönderen karar veremez;
  gönderen kaydı yoksa karar verilemez (fail-closed). Yetki `purchasing.requests.approve` (ALS-D2); audit aynı kayıtta.
- Talep geri çekme (A1-1b, ALS-D46): Gönderildi → Geri çekildi (`Withdrawn`; Reddedildi'den ayrı, kapanış durumu,
  siparişe dönüşmez). Yalnız talebi **gönderen** kişi, gerekçeyle (zorunlu, en fazla 4000 karakter) geri çeker; EK5
  notundaki "oluşturan/gönderen" ifadesi uygulamada en dar okumayla "gönderen" alınmıştır (oluşturan başka biriyse geri
  çekemez). Taslak, Onaylı, Reddedildi talep geri çekilmez; gönderen kaydı yoksa kimse geri çekemez (fail-closed);
  talebe bağlı Onaylı sipariş varken geri çekilmez (No.161 §1 koruması). Yetki `purchasing.requests.withdraw` (öneri;
  gerçek kullanıcıya grant insan kapısında); audit aynı kayıtta. Stok veya para olayı yok.
- Sipariş: Taslak → Onaylı (`PurchaseOrderStatus` Draft / Approved). Satır yalnız taslağa eklenir; onay tek adımlı
  geçiştir (onay politikası yok). Talebe bağlı sipariş yalnız Onaylı talepten açılır (ALS-D2, ALS-D16) ve talep Onaylı
  kaldığı sürece onaylanır; Σ-pay onay anında ölçülür.
- Kabul payı: yalnız Onaylı siparişin satırına; kalan = taahhüt − kabul; kalanı aşan kabul reddedilir (tolerans yok).
  Yeni kabul stoğa `Free` blokla girer. Genel/teknik kırılım Taslak ve Onaylı'da açıktır (yalnız domain).

**Hedef (karar var, kod yok):**

- Sipariş kalanı iptali: Onaylı siparişte, kısmi kabul olsa bile yalnız kabul edilmemiş kalanın **tamamı** iptal
  edilir; fiziksel stok veya para olayı yok; `Alış Kalan = Onaylı − İptal − KabulNet − KesinRed/İade` (No.161, INV-006).
- Satır kapanışı / yeniden açma: tolerans içi eksikte sistem kapatmayı önerir ve sorar (ALS-D10); tolerans dışı eksiği
  satın alma sorumlusu gerekçeyle kapatır (ALS-D12); kapanan satır gerekçeyle yeniden açılır. Stok/para olayı yok.
- Revizyon: onaylı siparişte değişiklik Rev+1 + yeniden onay; eski revizyon geçmişte; kabul edilmiş miktarın altına
  inilmez (ALS-D9). Sipariş onayında hazırlayan ≠ onaylayan, tutar eşiği Ayarlar'da başlangıçta kapalı (ALS-D3).
- Tedarikçi teyidi (ayrı eksen): Teyit bekliyor / Teyit edildi; isteğe bağlı, teyitsiz sipariş yine işler (ALS-D13).
  Ayrı "Rezerve" durumu açılmaz (ALS-D17).
- Tedarik ilerlemesi (satır): termin dilimleri (ALS-D33); isteğe bağlı ara adımlar "tedarikçide hazır" ve "yolda"
  (sevk bildirimi); sonra kabul. Tedarikçide hazır ve yolda miktar gelecek arzdır, fiziksel stok değildir (ALS-D25).
  Tedarikçide hazır miktar ve tarihi kabul tamamlanana kadar gerekçe + audit ile düzeltilebilir (ALS-D22, EK2 §2
  yorumu). Yolda miktarın düzeltme kuralı karara bağlanmadı (plan API-31).

## Diğer yaşam döngüleri

- Tahsis: Öneri → Taslak → Aktif → Kısmi Kullanıldı → Kullanıldı/Serbest/İptal
- Üretim planı: Taslak → Kontrolde → Onaylı → Kuyrukta → Üretimde → Kısmi/Tamamlandı → İptal/Ters
- Yükleme: Planlandı → Hazırlık → Yükleniyor → Tamamlandı → İptal
- Belge sürümü: Yüklendi → Sınıflandırıldı → Kontrolde → Onaylı → Geçersiz/Yeni Sürüm
- Finansal kaynak: Taslak → Kontrolde → Onaylı → Gerçekleşti → Mutabık → Ters
- Dağıtım: Öneri → Taslak → Uygulandı → Kısmi Geri Alındı → Geri Alındı

Her geçişte önkoşul, yetki, etkiler, ters alma ve audit şeması bulunur.
