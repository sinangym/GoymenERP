# Sinan Kararları — 2026-11-02 (Üretim EK3: Boyama, Soğuk Hadde ve Boru/Profil plan alanları)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda, U2-b (proses plan ayrıntıları) öncesinde
sorulan seçenekli sorulara verdiği **doğrudan cevaplar**. Ana karar dosyaları: `SINAN_KARARLARI_2026-11-01_URETIM.md`
(URT-D1…D18), `…_2026-11-02_URETIM_EK1.md` (URT-D19…D22), `…_2026-11-02_URETIM_EK2.md` (URT-D23, D24). Ana
dosyanın §0 yetki ve sınır kuralları aynen geçerlidir.

## 1. Kararlar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| URT-D25 | Boyama planında hesap alanları dışında girilecek bilgiler | **Boya cinsi / tedarikçi** planda seçilir (kullanılacak boya ürünü). **Renk kodu girilebilir;** varsayılan renk tanımlanabilir ya da renk not olarak yazılabilir (renk zorunlu değildir). Kat sayısı bu kararda istenmedi | Master §28.7 yalnız hesap alanları; StokERP'de renk/boya cinsi yoktu | PNT-001…006, PR-07 |
| URT-D26 | Soğuk hadde planının biçimi | **Şimdilik tek adım:** giriş kalınlığı → hedef kalınlık, planlanan fire. Çok geçişli (pass) plan daha sonra geliştirilebilir | StokERP tek adım | CR-001…005, PR-08 |
| URT-D27 | Boru/profil planında paket içi adetin kaynağı | **Ürün/paket kartından öneri, planda değiştirilebilir;** plan onayında plana sabitlenir | StokERP'de profil ölçüsü → standart paket tablosu | TUB-001…004, PR-06, Master §28.2 birim kg önceliği |

## 2. Uygulama

U2-b (`docs/delivery/URETIM_HAZIRLIK_PLANI_v1.md` API-12, MOD-02). Varsayılan renk bir Ayarlar değeri olarak tutulur
(kod sabiti değil); tanımlı değilse renk boş kalabilir.
