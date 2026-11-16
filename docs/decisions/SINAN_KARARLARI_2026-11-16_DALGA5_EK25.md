# Sinan Kararları — 2026-11-16 (Dalga 5 ek 25: W5-4 cevapları — bildirim saklama süresi, boya emri ölçümü, açılış dönemi sonrası kredi düzeltmesi, fazla teslim onay izni, SMM hesabı, kredi izinlerinin Critical sınıfı)

Bu belge önceki karar dosyalarını silmez. Şu kararları ekler: KLN-D114, URT-D109, FNS-D108, ALS-D116, SAT-D419 ve
FNS-D109. Her satır tamamladığı veya güncellediği önceki kararı adıyla söyler:

- KLN-D114 → KLN-D110 (EK20) ve KLN-D111 (EK23);
- URT-D109 → URT-D95 (EK15) ve URT-D108 (EK23);
- FNS-D108 → FNS-D85 (EK21) ve FNS-D101 (EK23);
- ALS-D116 → ALS-D114 (EK23);
- SAT-D419 → SAT-D413 (EK24);
- FNS-D109 → FNS-D100 (EK23), KIM-D52 (EK24) ve KIM-D9 (`SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md`).

Bu kararların burada değinilmeyen kısımları aynen geçerlidir. Diğer önceki kararlar da aynen geçerlidir.

EK15, EK20, EK21, EK23 ve EK24 henüz merge edilmemiş karar PR'larındadır (#1179, #1186, #1187, #1191, #1192). Bu
belge onlara o PR'lardaki metinle atıf yapar. Çelişkide daha yeni olan bu belge geçerlidir.

**Kaynak:** Sinan, 16 Kasım 2026, bu Claude Code oturumunda soru penceresiyle cevapladı (SINAN-CEVAP W5-4,
W5-4.1–W5-4.6): Issue #1189 yorumu
https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6920021736

Sorular, EK23 ve EK24'ün "Sınırlar" bölümlerinde `NEEDS-SINAN` olarak kalan noktalardan ve KLN-D110 saklama
sorusundan geldi. "(Önerilen)" işaretleri soranındır. Serbest metin cevap (W5-4.5) aynen alıntılanmıştır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| W5-4.1 | KLN-D110: Uygulama içi bildirimler ne kadar saklansın? | **90 gün (Önerilen)** | "Bildirimler 90 gün sonra pasife alınır; audit ayrı ve süresiz kalır." Seçilmeyen: "30 gün"; "1 yıl". |
| W5-4.2 | URT-D95: Boya emrinde iade üst sınırı ölçümü nerede yapılsın? | **Emir toplamında (Önerilen)** | "URT-D49 ile aynı, boya emrinin toplam girdi/çıktısı üzerinden; ana rulo ayrıca izlenmez." Seçilmeyen: "Ana rulo bazında". |
| W5-4.3 | FNS-D85: Açılış dönemi kapandıktan sonra açılış kredisi nasıl düzeltilsin? | **Ters+yeni kayıt, 2 kişi (Önerilen)** | "Kapalı dönem açılmaz; açık dönemde ters kayıt + doğru kayıt; biri girer, başka biri onaylar." Seçilmeyen: "Dönemi yeniden aç"; "Düzeltme yasak". |
| W5-4.4 | ALS-D114: Fazla teslim istisnasının onayı hangi izinle verilsin? | **Yeni ayrı izin (Önerilen)** | "Ör. `purchasing.receipts.overdelivery.approve`, sipariş onayından bağımsız." Seçilmeyen: "Mevcut `purchasing.orders.approve`". |
| W5-4.5 | SAT-D413: Emilemeyen geç fatura farkının yazılacağı SMM hesabı ne olsun? | Serbest metin: **"Mantıklı olan hangisi ize"** | Cevap seçeneklerden biri değildir; soranın kaydında önerilen seçeneğe bırakma olarak işaretlenmiştir. Önerilen seçenek: "Finans eşleme ayarından seçilir, varsayılan ticari mal 621, mamul 620; muhasebeci değiştirebilir." Diğer seçenekler: "Sabit 621"; "Muhasebeci belirlesin, o zamana kadar istisna". |
| W5-4.6 | `finance.credits.propose` / `finance.credits.approve` Critical sınıfı ne olsun? | **approve Critical, propose değil (Önerilen)** | `finance.credits.approve` Critical sınıfındadır; `finance.credits.propose` değildir. Seçilmeyen: "İkisi de değil"; "İkisi de Critical". Rol/grant ataması yapılmadı; insan kapısıdır. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KLN-D114 | Uygulama içi bildirim saklama süresi (W5-4.1; KLN-D110'u tamamlar, KLN-D111'deki süreyi günceller) | Uygulama içi bildirimler **90 gün** sonra **pasife alınır**. Audit kaydı bildirimden **ayrıdır ve süresiz** kalır. Bu süre KLN-D111'deki **1 yıl** başlangıç değerinin yerine geçer. KLN-D111'in kalıcı silmenin kapalı başlaması ve KLN-D110'un uygulama içi bildirim / "ayrım bırakıldı" işareti aynen geçerlidir. | Bildirim deposu, Ayarlar |
| URT-D109 | Boya emrinde iade üst sınırı ölçümü (W5-4.2; URT-D95'i tamamlar, URT-D108'deki `NEEDS-SINAN`'ı kapatır) | Boya emrinde URT-D95/URT-D108'deki iade üst sınırı **emir toplamında** ölçülür: URT-D49 ile aynı şekilde boya emrinin **toplam girdi/çıktısı** üzerinden. **Ana rulo ayrıca izlenmez.** URT-D108'deki formül aynen geçerlidir; hesap backend calculator'ındadır. | Üretim iade komutu (URT-S6/S2), boya emri, formül golden'ı |
| FNS-D108 | Açılış dönemi kapandıktan sonra açılış kredisi düzeltmesi (W5-4.3; FNS-D85'i tamamlar, FNS-D101'deki `NEEDS-SINAN`'ı kapatır) | Açılış dönemi kapandıktan sonra **kapalı dönem açılmaz**. Düzeltme **açık dönemde ters kayıt + doğru kayıt** ile yapılır. **Biri girer, başka biri onaylar** (giren onaylayamaz). FNS-D101'deki "açılış dönemi açıkken" kuralı dönem açıkken aynen geçerlidir. | FIN-S9 açılış bakiyesi, kredi açılış kaydı |
| ALS-D116 | Fazla teslim onay izni (W5-4.4; ALS-D114'teki izin adı `NEEDS-SINAN`'ını kapatır) | ALS-D114'teki fazla teslim onayı **yeni ve ayrı bir izinle** verilir; izin **sipariş onayından (`purchasing.orders.approve`) bağımsızdır**. Cevaptaki ad örnektir ("ör. `purchasing.receipts.overdelivery.approve`"); kesin izin adı uygulama ayrıntısıdır (geri alınabilir) ve kodlayan şeridin okumasıdır. ALS-D114'ün geri kalanı aynen geçerlidir. | Alış teslim girişi, fazla teslim onayı, izin kaydı (ErpPermissions, modül manifesti) |
| SAT-D419 | Emilemeyen geç fatura farkının SMM hesabı (W5-4.5; SAT-D413'ü tamamlar, EK24'teki eşleme `NEEDS-SINAN`'ını kapatır) | Serbest metin cevap: **"Mantıklı olan hangisi ize"**; önerilen seçenek uygulanır. SAT-D413'teki SMM farkının hesabı **Finans eşleme ayarından** seçilir. Varsayılan: **ticari mal 621**, **mamul 620**. **Muhasebeci** eşlemeyi değiştirebilir. SAT-D413'ün geri kalanı aynen geçerlidir. | Stok maliyet farkı (STK-SC36-a), Finans fiş önerisi, Finans eşleme ayarı |
| FNS-D109 | Kredi izinlerinin Critical sınıfı (W5-4.6; FNS-D100'ü tamamlar, KIM-D52'yi `finance.credits.approve` için günceller, KIM-D9 listesini genişletir) | **`finance.credits.approve` Critical sınıfındadır.** **`finance.credits.propose` Critical değildir.** KIM-D52'nin `finance.credits.approve` için "Critical değildir" ifadesi yalnız bu noktada güncellenir; KIM-D52'nin diğer izinleri aynen geçerlidir. Rol ve grant ataması yapılmaz (insan kapısı); grant yapılana kadar uçlar 403 döner. | Finans izin kaydı (ErpPermissions Critical sınıflandırması), kredi onay komutu |

Uygulama ayrıntısı (geri alınabilir): ALS-D116'daki kesin izin adı ve hata/durum kodlarının adları kodlayan şeridin
seçimidir.

## Sınırlar

- **Grant ve rol ataması:** `finance.credits.propose`, `finance.credits.approve` ve ALS-D116'daki yeni iznin persona,
  rol ve grant ataması bu belgede yapılmaz; ataması insan kapısıdır.
- **Critical sınıfının uygulanması (FNS-D109):** `ErpPermissions.Critical` kaydının değiştirilmesi ve sınıfın
  etkinleştirilmesi uygulama dilimine aittir; bu belge kod veya kayıt dosyası değiştirmez.
- **ALS-D116 yeni izninin Critical sınıfı:** seçilmemiştir → `NEEDS-SINAN`.
- **KLN-D114 kalıcı silme:** bu belge yalnız 90 gün sonra pasife almayı karara bağlar. Pasif bildirimlerin kalıcı
  silinmesi veya imhası ayrı politikadır; KLN-D111'deki gibi kapalı kalır ve açılması ayrı onay ister.
- **FNS-D108 izni:** düzeltmeyi giren ve onaylayan kişinin hangi izinle çalışacağı bu belgede seçilmemiştir →
  `NEEDS-SINAN`.
- **SAT-D419 eşleme ayarı yetkisi:** "muhasebeci değiştirebilir" ifadesi bir izin adı veya rol seçmez; ayarı
  değiştirme izni ve ataması → `NEEDS-SINAN` / insan kapısı.
- **`OPEN-*` kayıtları:** bu belgeyle hiçbir `OPEN-*` kaydı kapanmaz.
- İnsan kapıları değişmez:
  - deployment, environment ve secret;
  - Settings → Actions etkinleştirmesi;
  - grant/rol/Critical;
  - production aktivasyonu;
  - `OPEN-*` kapatma;
  - `.claude/settings.json`.
