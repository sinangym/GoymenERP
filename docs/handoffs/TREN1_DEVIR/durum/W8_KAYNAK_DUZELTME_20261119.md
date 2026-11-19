# W8 kaynak incelemesi sonrası düzeltmeler — 19 Kasım 2026

Bu kayıt mevcut W8 doğrulama/düzeltme kapsamındadır; yeni dalga veya yeni iş dilimi açmaz.
Kullanıcı sırası: doğrulanmış hataları birlikte düzelt, kaynağı yeniden incele, ardından düzeltilmiş
tek adaydan yeniden derleyerek test et. Eski aday için bekleyen takip başlatıcısı durduruldu.
Devam eden eski SQL koşusu ayrı tanısal kanıt olarak korunur; yeni kaynağın başarısı sayılmaz.

## Kaynak düzeltmeleri

| Bulgu | Değişiklik | Kaynak commit |
|---|---|---|
| F1 — Ayarlar provider hatası kalıcı conflict cevabına dönüşebiliyordu | 13 yönetim servisinde gerçek unique/concurrency ayrıldı; diğer SQL/EF hatası retry anahtarını tüketmeyen provider-unavailable olur. İlk receipt okuması ve catch içindeki winner okumasının SQL arızası da aynı yola gider. | `885fa0264` |
| F2 — Para profili yarış dönüşünde son yetki ölçümü eksikti | Son winner okumasından sonra şirket/aktör/yetki tekrar ölçülür; ikincil refusal kaydı öncesi de kontrol edilir. | `885fa0264` |
| F3 — Giriş başarı yazımı arada oluşmuş hesabı kilitleme kararını silebiliyordu | Normal/geçici parola başarı yazımı okunan lockout değeriyle sınırlandı. Paralel yeni kilit, parola rehash'i veya geçici parolanın tüketilmesiyle aşılmaz. Hatalı giriş sayacı değişmedi. | `d26f82de1` |
| F4 — Aynı maliyet girdisinde Pending→Recognized anahtarı çakışıyordu | İlk tarihsel anahtar korunur; sonraki geçişin kimliği beklenen Inventory sürümü, hedef durum ve girdi fingerprint'ini içerir. Eski receipt yeniden yazılmaz. | `461640edd` |
| F4 ek — Pending gözlem transit transfer kabulünü bozuyordu | Pending/Unreadable gözlem ve tersleme fiziksel etiket sürümünü artırmaz. Sürüm/ürün kontrolleri ve Serializable transaction korunur; Recognized parasal etkinin sürüm çiti sürer. | `461640edd` |
| F5 — Muhasebe store arızası sahte conflict veya kalıcı 422 üretebiliyordu | Eşleme ve katalog store'ları provider arızasını ayrı taşır; servis güncel yetkiyi tekrar ölçüp kalıcılaştırılmayan 503 döner. | `7a4498ce0` |
| F6 — Belirsiz bozdurma defter değeri komutu anahtarını kaybedebiliyordu | Exact şirket/aktör/oturum/namespace bağında yalnız bellekte anahtar ve gövde korunur. Route dönüşü ve belirsizlik sonrası erken ret aynı komutu korur; başka bağlamın geç yanıtı bırakılır. | `71b12045f` |

Ek SQL receipt-okuma regresyonu: `8a9231a4`. Altı F bulgusu ve F4'ün bağlantılı ek hatası
kaynakta düzeltildi. Bu ifade testlerin geçtiği veya bütün depoda başka hata olmadığı iddiası değildir.

Finance Q1 eşzamanlı reuse/cancel adayı değiştirilmedi: mevcut yeniden kullanım yolu yeni parasal
kalem oluşturmuyor veya iptal edileni açmıyor. Örtüşen kabulün iptalden önce sıralanması mümkün;
gerçek tüketici yanlış etkisi kanıtlanmadan politika veya kayıt davranışı değiştirilmedi.

## Yazılan doğrulama

- Sekiz yeni gerçek SQL fact: iki muhasebe provider-recovery, bir credential-lockout yarışı,
  bir gerçek transit/Pending/Recognized akışı, iki MoneyProfile winner-authority yarışı,
  bir MoneyProfile gerçek 1222 rollback/retry ve bir banka CSV receipt-okuma 1222/retry.
- Üretimde mevcut Unreadable/reopen/reclose testi fiziksel sürüm/zaman korunumuyla güçlendirildi.
- Kimlik ve muhasebe birim regresyonları ile maliyet geçiş anahtarı sözleşme testi yazıldı.
- Ekranda altı ek consumer regresyonu: erken 400/403, route dönüşü, şirket/aktör ayrımı,
  oturum sonu, eski bağlamın geç yanıtı ve doğrulanmış başarı sonrası anahtar temizliği.
- Yeni 46 hedeflik SQL listesi eski düzeltme örneklerini de içerir. Keşif/TRX kimlikleri birebir
  karşılaştırılır; eksik, fazla veya atlanan test başarı sayılmaz.

Bu belgenin kaynak kapanışı anında build/unit/SQL/Vitest **NOT_RUN**. Kaynak incelemesi ve
`git diff --check` tamamlandı. Çalıştırma kanıtı dondurulan adayın tam SHA'sına ayrı kaydedilecek;
başka SHA'nın ikilisi kullanılmayacak. Yerel SQL sürümü ile CI SQL sürümü farklı olduğundan
yerel sonuç, resmi CI veya tam K3 kabulünün yerine geçmez.

## K3 incelemesinde korunacak sınırlar

Yeni SqlClient yüzeyleri yalnız hata sınıflandırır: yeni connection, ham SQL komutu veya tenant
filtresi bypass'ı eklenmedi. Settings'teki 11 yeni sınıflandırma kaynağı ve Accounting mapping
store'u guard listesinde **K3 review pending** gerekçesiyle açıkça kayıtlıdır. Var olan CompanyPolicy,
WorkCenterCapacity ve Accounting catalog kayıtlarının gerekçeleri/pinleri de güncellendi.
Bu kayıtlar bağımsız inceleme imzası değildir; eski K3 bekleyen girdiler korunur.

Pending stok gözlemi, sahibin zorunlu Serializable transaction'ında okunan label/lineage satırlarını
korur. Kaynak dayanağı: [SQL Server SERIALIZABLE tanımı](https://learn.microsoft.com/sql/t-sql/statements/set-transaction-isolation-level-transact-sql#arguments).
Regresyon gerçek eşzamanlı label UPDATE'nin 1222 ile engellendiğini ve transaction sonrası gerçek
transfer kabulünün ilerlediğini doğrulamak üzere yazıldı; henüz çalıştırılmadı.

Ekran bellek kaydı browser storage'a yazılmaz. Sayfa oturum kapanırken unmounted ise eski kayıt
sonraki mount'ta budanır; farklı session/actor bağından geri okunamaz. Tam tarayıcı kabulü ayrıca gerekir.
Migration, işletme kararı, grant/rol/Critical, OPEN, production, Finance STOP veya workflow değişikliği yoktur.

## İlk doğrulama sonrası dar pin düzeltmesi

`b3f21f8f4` adayında derlemeler, 769 kimlik ve dört audit persistence testi geçti.
405 mimari testin biri, SQL doğrulayıcısının eski kaynak fingerprint'i nedeniyle başarısız oldu;
kuyruk SQL hedeflerini başlatmadan durdu. Bu sonuç tam kabul değildir.

Kök neden: `9077fdf4f` süre aşımında etkin global/process bütçesini raporlarken
`ReviewedSqlServerRcsiVerifierFingerprint` güncellenmemişti. Kaynak farkı ve mevcut
timeout/redaction regresyonu okundu: toplam 10800 saniye ve süreç başına 2400 saniye,
exact discovery, skip0, hata sonrası proof yasağı değişmiyor. Pin ev üreticisinin
`7982b720…` çıktısına güncellendi; başka guard koşulu veya izin listesi değiştirilmedi.

Bu ekin yazıldığı anda düzeltilmiş adayın testleri henüz çalıştırılmadı. Eski sonuçlar korunur;
yeni kanıt yeni tam SHA'ya bağlanır. Bağımsız exact-head K3 incelemesi ayrıca bekler.
