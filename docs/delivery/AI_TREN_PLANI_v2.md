# Sistem yapay zekâsı — tren planı v2

Kaynak kararlar: `SINAN_KARARLARI_2026-11-22_AI.md` (AI-D10–AI-D15), DEC-AI-001..009, AI-D1..AI-D9, 156/157/159, E9.
Önceki plan `AI_MERKEZI_PLAN_v1.md` (AI-1..AI-10) korunur; bu belge onu kapsamı genişleterek günceller. Uygulama, mevcut
tren (PR #1201) ana dala girdikten sonra ayrı trende yapılır (AI-D10).

## 1. Bugünkü durum (kod, 2026-11-22)

- Altyapı var: `GoymenERP.Api/Ai` (yetki/şirket/veri sınıfı kontrolü, `ai.tool.called` denetimi), `BuildingBlocks/Ai`
  (Read/Draft/Simulate/Write araç sözleşmeleri, önizleme ve komut bağlamı), sağ panel (`frontend/src/ai`).
- Kapalı: `Ai:Enabled=false`, dış çağrı taşıyıcısı yok, sağlayıcı anahtarı yok; model yalnız araç planlıyor, doğal dil cevap
  üretmiyor.
- Boş: kayıtlı araç sayısı **0**; bilgi tabanı, hafıza, geri bildirim döngüsü yok; onay kutusu yalnız 4 onay türünü bağlıyor.

## 2. Hedef mimari

1. **Ağ geçidi (AI-D11):** model-bağımsız; görev türüne göre model seçimi; değerlendirme testleriyle (şirketin gerçek soru ve
   belge seti) ölçülen kalite; çıkış filtresi (takma ad, hassas veri reddi) ve sağlayıcı saklamaz/eğitmez şartı.
2. **Bilgi tabanı (AI-D12, "sistemi bilmesi"):** kararlar (`docs/decisions`), modül README'leri, formül golden'ları, durum
   makineleri, onay matrisleri, ekran kataloğu ve yardım metinleri sürümlü olarak indekslenir. Her bilgi parçası: kimlik,
   kaynak, geçerlilik, yerine geçtiği sürüm. Yeni karar ana dala girince otomatik yeniden indekslenir; aynı bilgi tekrar
   eklenmez, değişen bilgi eski sürümü "yerine geçildi" yapar. Cevaplar kaynak/karar numarasıyla verilir.
3. **Öğrenme döngüsü (AI-D12):** onay/red gerekçeleri, düzeltilen taslaklar, "faydalı/faydasız" geri bildirimi ve sık sorular
   toplanır; tekrarlanan örüntüden "kural önerisi" çıkar; yönetici onaylarsa sürümlü politika olur (DEC-AI-006). Kişisel
   tercihler (dil, özet biçimi, ilgi alanları) kullanıcı hafızasına yazılır.
4. **Araç katmanı ("davranması"):** her modül sahibi kendi okuma araçlarını açar (yetki ve alan izin listesiyle); sonra taslak
   ve önizleme araçları; yazma her zaman önizleme + kullanıcı onayı + idempotency + denetim ile, hesap her zaman deterministik
   backend calculator'ında. Onay komutları yapay zekâya açılmaz.
5. **Kişisel asistan (AI-D15):** tek sistem yapay zekâsı, kullanıcının yetkisi ve kendi hafızasıyla; her ekranda bağlama
   duyarlı (açık belge, seçili satır), sağ panelde sohbet, ayrıca sistem dışı genel/sektörel sohbet modu.
6. **Zamanlı görevler (AI-D15):** kullanıcı "her gün 08:30'da", "her pazartesi", "şu olay olunca" diye görev kurar; görev
   okuma araçlarıyla çalışır, sonucu bildirim/özet olarak verir, yazma yapmaz. Örnekler §4'te.
7. **Denetim ve kimlik:** her adım (soru, kullanılan bilgi, araç ve sürüm, öneri, kullanıcı kararı, ERP etkisi) DEC-AI-005'e
   göre kaydedilir; ajanın kendi kimliği ve en az yetkisi; zamanlı görev kullanıcının yetkisiyle sınırlıdır.

## 3. Modül bazında yetenekler (ilk tren: okuma/açıklama/taslak; yazma yok)

| Modül | Kullanıcıya | Onaycıya |
|---|---|---|
| Satış | Siparişin durumu ve neden takıldığı (risk, iskonto, tahsis, tartım); e-posta/PDF'ten sipariş/teklif taslağı; müşteri geçmişi ve fiyat önerisi açıklaması; adetli siparişte teorik/gerçek kg fiyatı yorumu | İç onay, iskonto/taban fiyat istisnası, risk istisnası: özet, kural kontrolü (SAT-D*), müşteri risk ve marj etkisi, benzer geçmiş kararlar |
| Alış | Talep/sipariş durumu, tedarikçi teyidi eşleştirme, geciken teslim listesi, gelen fatura taslağı ve eşleştirme farkı açıklaması | Talep/sipariş onayı, fatura farkı, iade maliyet istisnası (SAT-D392/D393): fark kaynağı, tolerans, tedarikçi geçmişi |
| Stok | Etiket soyu ve maliyet açıklaması, sayım farkı nedeni, tahsis edilebilir stok, adet/kg tutarlılığı | Sayım, hurda, transfer kaybı/fazlası, açılış maliyeti onayı: fark büyüklüğü, tarihsel ortalama, soy ağacı |
| Üretim | Plan/emir durumu, darboğaz ve gecikme riski, çıktı kg/adet sapması (URT-D112), fire açıklaması | Plan onayı, maliyet farkı: girdi-çıktı dengesi, sapma nedenleri |
| Lojistik | Yükleme talimatı hazırlığı, tartım farkı açıklaması, sevk planı aşımı | Tartım farkı, plan aşımı, müşteri iadesi onayı |
| Finans | Cari bakiye ve vadesi geçenler, tahsilat önceliği, kredi limiti kullanımı | Kredi limiti, ödeme, teminat, açılış bakiyesi: risk özeti, limit geçmişi, SoD uyarısı |
| Muhasebe | Fiş kaynağı açıklaması, dönem kapanış engelleri listesi | Kaynak fiş, dönem onayı, SMM/maliyet farkı önerileri: tutar kaynağı ve kural |
| Ürün/Cari | Mükerrer kart ve benzer ürün önerisi (KLN-D40, URN-D25), eksik ölçü/ağırlık uyarısı | Paket standardı, unvan/VKN değişikliği onayı: değişiklik özeti |
| Ayarlar | Politika açıklaması ("bu eşik neden var") | Politika yayımlama: değişikliğin etkileyeceği işlemler |
| Kimlik | "Bu ekranı neden göremiyorum" açıklaması | Yetki talebi onayı: istenen yetkinin kapsamı ve SoD çakışması |

## 4. Modüller arası yetenekler

- **Siparişten nakde zinciri:** "Bu sipariş neden faturalanmadı?" → satış onayı, tahsis, yükleme, tartım, fatura, Finans
  durumunu tek cevapta birleştirir.
- **Tedarikten maliyete zinciri:** "Bu ürünün maliyeti neden arttı?" → alış faturası, gerçek kg (ALS-D119), kur, fire, iade
  farkı.
- **Sabah özeti (zamanlı görev):** kullanıcının rolüne göre bekleyen onaylar, geciken siparişler, risk aşan cariler, sayım
  farkları.
- **Olay güdümlü uyarılar:** tedarikçi teslim gecikmesi, kredi limiti eşiği, tartım farkı tolerans dışı, marjı negatife düşen
  sipariş.
- **Kapanış asistanı:** dönem kapanışı öncesi bütün modüllerdeki engellerin listesi ve sorumlusu.
- **Sistem dışı:** sektörel sohbet ve araştırma (ör. çelik/emtia fiyat eğilimleri, mevzuat değişiklikleri), şirket verisine
  dokunmadan; şirket verisiyle birleştirme AI-D3/D4 kurallarıyla.

## 5. Dilimler (sonraki tren)

| # | Dilim | İçerik |
|---|---|---|
| AI-11 | Ağ geçidi + değerlendirme | Model-bağımsız çağrı, doğal dil cevap, değerlendirme seti, maliyet/kalite ölçümü (anahtar ve açma insan kapısı) |
| AI-12 | Bilgi tabanı | Karar/README/golden/ekran indeksleme, sürümleme, tekilleştirme, kaynaklı cevap |
| AI-13 | Okuma araçları | Her modülden 3–6 okuma aracı (§3), yetki ve alan izin listesiyle |
| AI-14 | Onay kutusu yardımcısı | Bütün onay türlerini Work kutusuna bağlama + özet/risk/öneri kartı (AI-D13) |
| AI-15 | Belgeden taslak | E-posta/PDF → satış siparişi, tedarikçi teyidi, gelen fatura taslağı (insan onaylı) |
| AI-16 | Doğal dille sorgu | Yetki sınırında sorgu ve rapor, kaynaklı sonuç |
| AI-17 | Kişisel asistan + hafıza | Kullanıcı hafızası, bağlama duyarlı panel, sistem dışı sohbet modu |
| AI-18 | Zamanlı görevler | Görev tanımı, zamanlayıcı, olay tetikleyicileri, bildirim; yalnız okuma |
| AI-19 | Öğrenme döngüsü | Geri bildirim, kural önerisi → yönetici onayı → sürümlü politika |
| AI-20 | AI Merkezi ekranları | AI-01/02/03: kullanım, denetim, politika ve bilgi tabanı yönetimi |
| AI-21 | Konuşarak ayar/yetki (AI-D16) | Ayar değişikliği ve yetki talebi taslağı, önizleme, etki özeti; uygulama yetkili onayıyla |
| AI-22 | Zamanlı dış e-posta (AI-D17) | E-posta entegrasyonu, toplu onay ekranı, yanıt sınıflandırma ve üst kullanıcı raporu |
| AI-23 | Güvenlik/teknoloji takip (AI-D18) | Açık/haber/olay izleme, çözüm önerisi, değişiklik önerisi (PR), önceden onaylı acil adımlar |

## 6. Açık kararlar (sonraki trenden önce sorulacak)

- Aday modeller ve değerlendirme ölçütleri; sağlayıcı ve KVKK (OPEN-020); yerel model (OPEN-014).
- Sistem dışı web araştırması sağlayıcısı ve şirket verisiyle birleştirme sınırı.
- Kişisel hafıza saklama süresi ve kullanıcının silme hakkı.
- Hangi onay türlerinde ileride politika sınırlı otomatik onay açılacağı ve eşikleri.
- `ai.panel.use` dışında yeni izinler (zamanlı görev, belge taslağı) ve hangi rollere verileceği (insan kapısı).
