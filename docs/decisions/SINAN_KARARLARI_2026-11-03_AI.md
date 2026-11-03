# Sinan Kararları — 2026-11-03 (AI katmanı: dış AI veri politikası ve "genel AI" ihtiyacı)

Bu belge önceki karar dosyalarını silmez; AI-D1…AI-D6'yı ekler. `DEC-AI-001…009`, `AI_VERI_GUVENLIK.md`,
`AI_YETKI_SOZLESMESI.md`, `ERP-000J-AI-CONTRACT` ve `SINAN_KARARLARI_2026-10-21.md` No 156/157/159 geçerlidir.

**Kaynak:** Sinan Göymen'in 3 Kasım 2026'da Claude Code cloud oturumunda (AI Merkezi şeridi,
oturum `session_015Usc2puiKz7TEEqG49ym5e`) yazdığı görev metni. Tırnak içindeki ifadeler Sinan'ın metninden
aynen alınmıştır; "Uygulama notu" sütunu Claude'un yorumudur ve geri alınabilir.

## 0. Yetki ve sınır

- Bu belge, AGENTS.md "Çelişkide daha yeni kullanıcı kararı geçerlidir" kuralıyla AI kapsamında
  `KARAR_SICILI.md` ve master rapordan daha yenidir. `DEC-AI-001…009` ile çelişmez; `DEC-AI-004` ve
  `DEC-AI-009`'daki "bulut yalnız veri sınıfı ve yönetici politikasıyla" kuralının **içeriğini** belirler.
- **OPEN-020 KAPANMAZ.** Sağlayıcı seçimi, sözleşme/DPA, KVKK aktarım zemini, gerçek hesap/proje
  attestation'ı ve canlıda açma Sinan'da kalır. Bulut AI çağrısı, bu kapılar kapanana kadar **default-deny**
  (kapalı) kalır; geliştirme ve test yalnız sahte (fake) sağlayıcıyla yapılır.
- Dış AI'ın canlıda açılması, veri türü izinlerinin verilmesi ve rol/yetki ataması **insan kapısıdır**
  (Sinan). Bu belge permission adı, rol, grant, model, sağlayıcı veya ücretli hizmet seçmez.
- API anahtarı/secret repoya girmez; yalnız ortam değişkeni veya secret deposu adı (placeholder) yazılır.

## 1. Sinan'ın metni (aynen)

> İstenen AI: sistemi kullanabilen (kullanıcı onayıyla işlem yapar), iş yükünü azaltan (taslak, OCR, özet),
> sistemi tanıyan (ekran, kural ve kaydı açıklar), genel sorulara da cevap veren; hem yerel sistem AI'sı hem
> API ile bağlı dış AI.

> Dış AI veri politikası (Sinan, 3 Kasım): hassas olmayan veri gidebilir. Hassas veri (fiyat, bakiye, IBAN,
> finans, şirket belgeleri) yalnız maskelenmiş (takma ad) olarak ve yöneticinin Ayarlar'dan açtığı veri
> türlerinde gider. Sağlayıcı veri saklamayan/eğitimde kullanmayan olmalı.

> Dış AI'ın canlıda açılması, veri türü izinleri ve rol/yetki Sinan'da kalır.

## 2. Kararlar

| No | Konu | Karar | Uygulama notu (geri alınabilir) | Bağlı kimlik |
|---|---|---|---|---|
| AI-D1 | AI'ın dört işi | AI (1) **sistemi kullanır**: kullanıcı onayıyla işlem yapar; (2) **iş yükünü azaltır**: taslak, OCR, özet; (3) **sistemi tanır**: ekran, kural ve kaydı açıklar; (4) **genel sorulara** da cevap verir | (1) DEC-AI-002/003 zinciriyle: önizleme + açık kullanıcı onayı + idempotency; AI kendi onaylayamaz. (4) Genel soru ERP verisi içermez; ERP verisi gerekiyorsa yalnız araçla ve kullanıcının yetkisi kadar | DEC-AI-002, DEC-AI-003, DEC-AI-008 |
| AI-D2 | İki AI kaynağı | **Yerel sistem AI'sı** ve **API ile bağlı dış AI** birlikte vardır | Yerel AI: ERP verisi şirket içinde kalır; kullanıcının yetkisi kadar tam veri görebilir. Dış AI: AI-D3/D4 süzgecinden geçer. Hangisinin kullanılacağını sunucu seçer, model seçemez. İkisi de kapalıyken ERP normal çalışır (DEC-AI-001) | DEC-AI-001, DEC-AI-009, OPEN-020 |
| AI-D3 | Hassas olmayan veri | Dış AI'a **hassas olmayan veri gidebilir** | Hassas olmayan = AI_VERI_GUVENLIK "Genel" ve "Şirket içi" sınıfı (ör. ekran/alan açıklaması, durum adı, kural metni, ürün adı, miktar/birim, genel soru). Görev için gereken en az veri gider (minimizasyon); yetkisiz satır hiç gitmez | AI_VERI_GUVENLIK §Sınıflar |
| AI-D4 | Hassas veri | Hassas veri — **fiyat, bakiye, IBAN, finans, şirket belgeleri** — dış AI'a **yalnız maskelenmiş (takma ad)** olarak ve **yöneticinin Ayarlar'dan açtığı veri türlerinde** gider | Maskeleme sunucuda, gönderimden önce yapılır; ham değer dışarı çıkmaz, takma ad ↔ değer eşlemesi yalnız ERP içinde ve o istek için tutulur; dönüşte takma ad sunucuda geri çözülür. Veri türü izni varsayılan **kapalı**dır; tür başına açılır (ör. "fiyat", "bakiye", "IBAN", "finans tutarı", "şirket belgesi"). İzin açık olsa da ham hassas değer gönderilmez. IBAN, AI_VERI_GUVENLIK'te "Çok gizli" olsa da bu daha yeni kararla yalnız takma ad olarak gidebilir (ham IBAN hiçbir koşulda gitmez). Sinan'ın listesinde olmayan "Çok gizli" kalemler (kimlik no, bordro, erişim anahtarı/secret) hiçbir izinle dışarı gitmez | DEC-AI-004, DEC-AI-007, AI_VERI_GUVENLIK, SINAN_KARARLARI_2026-10-21 No 135 (maskeleme alanları) |
| AI-D5 | Sağlayıcı şartı | Dış AI sağlayıcısı **veri saklamayan ve eğitimde kullanmayan** olmalıdır | Sağlayıcı bağlantısı ancak bu şart sözleşme/hesap ayarıyla kanıtlanınca etkinleştirilebilir (ERP-000J §15 attestation). Kanıt yoksa bağlantı fail-closed kapalı kalır; sağlayıcı seçimi Sinan'dadır | OPEN-020, ERP-000J §15 |
| AI-D6 | İnsan kapısı | Dış AI'ın **canlıda açılması**, **veri türü izinleri** ve **rol/yetki** Sinan'dadır | Ayarlar'daki AI politikası (dış AI açık/kapalı, izinli veri türleri) normal ayar politikası akışıyla (hazırla → onayla → yayınla, ADR-SET-002) yönetilir; canlı ortamda ilk açma Sinan'ın açık kararıdır. Geliştirme bu ayarların **kayıt ve okuma** yolunu yazar, değerlerini seçmez | DEC-SET-001, ADR-SET-002, AGENTS.md insan kapıları |

## 3. Şerit

Sinan'ın 3 Kasım görev metniyle AI katmanı (sağ AI paneli, AI Merkezi — günlük özet/öneri/geri bildirim —,
modül araç kataloğu, belge okuma/OCR, dış AI bağlantısı) **AI Merkezi şeridinde (Claude)** geliştirilir.
Modül araçları, sahip modülün mevcut public application query/command portunu sarar; sahip modülün iş kuralı,
formülü veya tablosu AI şeridinde değiştirilmez, gereken port ilgili şeride görev notuyla istenir.
Review ve merge kapıları değişmez (K3'te iki bağımsız exact-head review).

## 4. Açık kalanlar (NEEDS-SINAN, Issue #66)

- Dış AI sağlayıcısı ve hesap (veri saklamama / eğitimde kullanmama kanıtıyla) — OPEN-020.
- Yerel model/OCR çalışma ortamı ve GPU kapasitesi — OPEN-020 (yerel PoC).
- Ayarlar'daki AI politikasını hazırlama/onaylama/yayınlama izinleri hangi personalarda — rol/yetki kapısı.

Not: genel (ERP dışı) soru hassas veri içermediği için AI-D3 kapsamındadır; dış AI canlıda açıldığında ayrı
veri türü izni istemez. Soru metninde hassas değer (ör. IBAN) algılanırsa AI-D4 süzgeci uygulanır.
