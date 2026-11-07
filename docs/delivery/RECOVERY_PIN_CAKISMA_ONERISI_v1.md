# Recovery ve parmak izi pin'lerinin PR'ları kilitlemesi — analiz ve öneri v1

- **Tarih:** 2026-11-07
- **Durum:** Öneri. Kod değişikliği yok. **Karar Sinan'da.**
- **Hazırlayan:** Kalan Modüller şeridi (Claude)
- **Kaynak commit:** `origin/main` `1e694aa3e` (#929)

## 1. Sorun, tek paragraf

Backend'e dokunan her merge, açık bütün backend PR'larını bozuyor. Sebep: depoda bütün backend'in
tek bir özet değerine (hash) bağlandığı birkaç "global pin" var. Main'e bir PR girince bu değerler
değişiyor; diğer her PR main'i alıp aynı değerleri yeniden üretmek, yeniden CI koşturmak ve
(K3'te) yeniden review almak zorunda kalıyor. 20 Ekim'den beri main'e giren 75 commit'in 21'i
`src/backend/`'e dokundu ve bu 21'in hepsi `tools/generate_recovery_surface_inventory.py`'yi de
değiştirmek zorunda kaldı; commit gövdelerinde ≈55 "re-pin" satırı var (`ORTAK_DESENLER_v1.md` P1).
Sonuç: PR'lar fiilen tek sıra halinde merge ediliyor.

## 2. Pin'ler neden var ve hangisi gerçekten "insan onayı" taşıyor

**(a) = ağaçtan mekanik türetilen özet** (değeri bir araç basar, insan kopyalar). **(b) = incelenmiş
izin listesi** (bir satırın eklenmesi/değişmesi, reviewer'ın bilerek "evet" demesi gereken bir olaydır).

| Pin | Ne yakalar (amaç) | Sınıf | Çakışma sıklığı |
|---|---|---|---|
| `EXPECTED_SOURCE_SURFACE_FILE_COUNT` / `_FINGERPRINT` (generator + recovery testi) | `src/backend/**` ve build dosyalarının **her baytı**. Generator'daki gerekçe: bağlı dosya (linked file), source generator, global alias veya isim gölgeleyen bir dosya, sözcüksel (regex) envanteri sayıları değiştirmeden atlatmasın. | (a) | **Her backend PR'ında** — asıl kilit bu |
| `EXPECTED_MIGRATION_COUNT` / `_DECLARATION_FINGERPRINT` (generator, şema `const/minItems/maxItems`, iki test) | İncelenmemiş migration'ın envantere girmeden geçmesi | (b) içerik, (a) biçim | Her migration ekleyen PR |
| `EXPECTED_DB_CONTEXT_*`, `EXPECTED_DURABLE_STORE_COUNT` | Fark edilmeden yeni DbContext / kalıcı depo eklenmesi | (b) | Nadir |
| `REVIEWED_OPERATIVE_METHOD_FINGERPRINTS`, `REVIEWED_LIVE_SOURCE_SHA256` | İncelenmiş store metodunun (ör. `PurchasingStore.StoreRequestAsync`) sessizce değişmesi | (b), dosya başına | Yalnız aynı store'a dokunan iki PR |
| `EXPECTED_AUDIT_LIVE_SOURCE_*` | Audit modülünün canlı kodunun sessiz değişmesi | (b), modül başına | Nadir |
| `PlatformTenantQueryFilterBypassGuardTests.cs` sözlükleri | Şirket (tenant) filtresini atlayan ham SQL, DbContext, `migrationBuilder.Sql`, modül bağlantısı. Dosya yolu + API sayısı (`SqlClient = 1`, `Sql = n`) reviewer gerekçesi ister. | (b), dosya başına | Aynı sözlükteki komşu satırlar yüzünden metin çakışması |
| `tools/relational_inventory/<modül>.txt` + merkezi parmak izi | Gerçek SQL kabul testlerinin listesi; test sessizce düşmesin | (b) liste, (a) merkezi hash | Aynı modül + merkezi pin |
| `ShellCapabilityRegistryAuthorities.cs` | Kabul edilmiş shell yetenek bileşimleri (frontend/backend sözleşmesi) | (a) değer, (b) sağlayıcı sürümü | Her sağlayıcı sürüm artışında 9 satırın hepsi |

**Gözlem 1.** Asıl insan kararını taşıyan şeyler (b) satırları: yeni migration, yeni DbContext, yeni
bypass dosyası, değişen incelenmiş metot. Bunlar zaten dosya/modül bazında ve nadiren çakışıyor.
Her PR'ı kilitleyen ise (a) türü global bayt özeti. Bu değer her backend PR'ında değiştiği için
PR diff'inde **hiçbir sinyal taşımıyor**: reviewer'ın görebileceği tek şey "araç yeni değer bastı"
oluyor (`print_recovery_actuals.py` çıktısı kopyalanıyor).

**Gözlem 2 (önemli).** `ci.yml` bütün işleri **PR head'i** üzerinde koşuyor (`pull_request.head.sha`),
main ile birleşmiş sonuç üzerinde değil. Global pin bugün bu boşluğu dolaylı kapatıyor: main
ilerleyince pin tutmaz, PR'ı güncellemek zorunlu olur; böylece "CI'ın gördüğü ağaç = main'e giren
ağaç" garanti olur. Global pin kaldırılırsa bu yan faydanın **başka bir kapıyla** korunması gerekir;
aksi halde her biri tek başına yeşil iki PR, birlikte hiç test edilmemiş bir main üretebilir.

## 3. Seçenekler

| Seçenek | Kazanç | Kayıp / risk | Kapı |
|---|---|---|---|
| **A. Modül başına pin dosyaları** (ör. `contracts/recovery/pins/<modül>.json`; guard sözlüklerini mevcut `partial` desenine göre modül dosyalarına bölmek) | Çakışma yalnız aynı modüle dokunan PR'lar arasında. Güvenlik kaybı yok. | `GoymenERP.Api` (endpoint'ler) her şeridin ortak modülü; onun parçası yine sık çakışır. Global "toplam" hash kalırsa kazanç sıfır. Şema değişir. | K3 (kod+test+şema), iki review |
| **B. (a) türü global özeti PR'dan çıkarıp CI'da merge sonucuna göre hesaplamak** (merge queue veya merge ref üzerinde zorunlu kontrol) | Kilit tamamen kalkar; kanıt her merge sonucu için üretilir. | GitHub merge queue büyük ihtimalle **kullanılamaz** (depo kullanıcı hesabına ait: `sinangym/GoymenERP`; merge queue organizasyon depolarında var). `merge_group` tetikleyicisi ve Settings'te zorunlu kontrol ayarı gerekir. | `.github/**` = K3 iki review + Settings = **insan kapısı** |
| **C. Merge sonrası bot commit'i** (main'e otomatik re-pin) | Şeritler pin'le hiç uğraşmaz. | `contents: write` token gerekir; mevcut CI testleri yazma yetkisini bilerek reddediyor. `GITHUB_TOKEN` ile atılan commit yeni CI tetiklemez, yani bot commit'i doğrulanmadan main'e girer; tetiklemek için PAT (secret) gerekir. Main, merge ile bot arasında kırmızı kalır; bot review'suz main'e yazar. | Secret/token = **insan kapısı**. **Önerilmez.** |
| **D. PR'da yalnız (b) pin'leri tutmak; global bayt özetini pin olmaktan çıkarıp envanter/manifest çıktısına kanıt olarak yazmak** | Kilit kalkar; insan onayı gereken her şey (migration, DbContext, store, bypass, incelenmiş metot) aynen kalır. Bayt özeti kaybolmaz, her commit'in envanterinde ve release manifest'inde kayıtlı olur (`sourceCommit` + tree zaten bağlı). | Generator'daki "linked file / source generator / global alias" korkusu için yerine **dar bir riskli-yüzey pin'i** konmalı: `*.csproj`, `Directory.*`, `global.json`, `NuGet.Config`, `global using` içeren dosyalar ve hedef tiplerden (`DbContext`, `Migration`, `IDocumentObjectStoreV1`) türeyenlerin listesi. Bu, nadiren değişir ve değiştiğinde gerçekten bakılmalıdır. Gözlem 2'deki yan fayda kaybolur → ayrı kapı gerekir. | K3 (tools/tests/contracts), iki review. Bir güvenlik kapısını daralttığı için Sinan onayı önerilir (Soru 1). |
| **E. `.gitattributes merge=union`** (`relational_inventory/*.txt` gibi sıralı satır listeleri) | Yerelde aynı modüle iki test ekleyen PR'lar çakışmasız birleşir. | Sıra bozulabilir, silinen/yeniden adlandırılan satırın eskisi kalabilir. Doğrulayıcı (sıra + keşif kontrolü) bunu **reddeder**, yani güvenli ama gürültülü. GitHub'ın PR birleştirmesinin bunu uygulayıp uygulamadığı doğrulanmadı. Merkezi parmak izi yine çakışır. Hash dosyalarında **asla** kullanılmaz. | K1/K2, düşük değer |

## 4. Öneri — fazlı

**Faz 0 — hemen, kapı yok (süreç).** Koordinatör merge'den önce `git merge-base --is-ancestor origin/main <head>`
kontrolünü kanıt olarak PR'a yazar. Şeritler main'i alırken `main-merge-resync` sırasını izler.
Birden çok PR bekliyorsa önce (b) değişikliği olmayan, küçük PR'lar merge edilir; resync'ler toplu yapılır.
Bu kilidi kaldırmaz, yalnız sırayı ucuzlatır.

**Faz 1 — K3 kod PR'ı, iki review, `.github/**` yok (Soru 1'e bağlı).** Seçenek D:
1. `EXPECTED_SOURCE_SURFACE_FILE_COUNT` / `_FINGERPRINT` sabitlerini kaldır; yerine dar riskli-yüzey
   pin'i koy (yukarıdaki dosya sınıfları). Tam bayt özeti envanter JSON'una `sourceSurface` alanı olarak
   **çıktı** olsun ve release manifest'e geçsin (kanıt kaybolmaz, yalnız sabit olmaktan çıkar).
2. Migration/DbContext/store sayıları ve bildirim parmak izleri, incelenmiş metot ve blob hash'leri
   olduğu gibi kalır (bunlar (b)).
3. Gözlem 2 için: CI'a eklenmeden de korunabilsin diye Faz 0'daki "head main'i içeriyor" kontrolü
   merge kapısının yazılı parçası olur.

Beklenen etki: sıradan bir endpoint/iş kuralı PR'ı artık hiçbir recovery pin'ine dokunmaz; yalnız
migration, DbContext, incelenmiş store veya proje/build dosyası değiştiren PR'lar pin taşır.

**Faz 2 — K3, iki review.** Seçenek A'yı (b) pin'lerine uygula: guard sözlüklerini ve migration
listesini modül dosyalarına böl (guard'da `partial` deseni zaten var: Audit/Counterparty/Idempotency).
Migration sayısı modül başına tutulur, toplam CI'da hesaplanır. Shell registry için ayrı bir küçük
çalışma: sağlayıcı sürümü (b) pin olarak kalsın, bileşim hash'i testte türetilsin (sözleşme etkisi
nedeniyle K3).

**Faz 3 — yalnız Sinan isterse.** Merge sonucu üzerinde zorunlu CI (Seçenek B) veya Settings'te
"dal main ile güncel olmalı" kuralı. `.github/**` değişikliği iki review ile gelir; Settings ayarı
insan kapısıdır. Seçenek C (bot commit) secret gerektirdiği ve review'suz main'e yazdığı için önerilmez.

## 5. NEEDS-SINAN soruları

1. **NEEDS-SINAN: Bütün backend'in tek bayt özetini PR'da sabit tutma şartını kaldırıp yerine "dar riskli
   dosyalar" pin'i + envanter/manifest kaydı koyalım mı?** — Önerilen cevap: **Evet.** Yeni migration,
   veritabanı bağlamı, ham SQL ve incelenmiş metotlar yine insan onayı ister; kaybolan tek şey her PR'da
   anlamsızca değişen bir sayının elle kopyalanması.
2. **NEEDS-SINAN: GitHub Settings'te "Require branches to be up to date before merging" açık mı, açalım mı?**
   — Önerilen cevap: **Şimdilik açmayalım;** aynı güvenceyi koordinatörün merge öncesi yazılı "head main'i
   içeriyor" kontrolüyle sağlayalım. Sorun sürerse Settings'ten açarsınız (yalnız siz açabilirsiniz).
3. **NEEDS-SINAN: Main'e otomatik commit atan bir bot (yazma yetkili token) ister misiniz?** — Önerilen
   cevap: **Hayır.** Secret gerektirir, attığı commit CI'dan geçmeden main'e girer ve review'suz yazar;
   Faz 1–2 aynı rahatlığı token olmadan sağlar.
