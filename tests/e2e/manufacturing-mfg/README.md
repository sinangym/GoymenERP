# MFG: üretim emri ekranı, gerçek tarayıcı kabulü (`browser:mfg:ci`)

**Sahip:** ORGANİZASYON (Manufacturing).
**Ekranlar:** MFG-UI #323 `/uretim/emir/:orderId`; MFG-ORD `/uretim/emirler` (emir listesi).
**Kaynak:** koordinatör kararları D1–D5, Business #66 5734735965 (gerçek GET/HTTP; 503'lü form kullanıcı teslimi sayılmaz).

## Ne ölçer
- **Stack:** gerçek Chromium, exact head'in web build'i, gerçek API, gerçek SQL Server, gerçek Settings kg profili.
- **Emir hazırlığı:** her vaka kendi emrini **gerçek Manufacturing API** üzerinden, seed'in recorder aktörüyle hazırlar: plan → onay → emir. SQL'e doğrudan yazılmaz.
- **Vakalar:**

| Vaka | Kanıt |
|---|---|
| Kayıt | Formdan girdi çıkışı. POST'ta kendi anahtarı ve sürümü; ardından emir GET ile yeniden okunur; tablo sunucu değerini gösterir; API okuması aynı değer. **Kütle dengesi paneli (GDC-BAL/1):** olgudan sonra kendi GET'ini yeniden okur; BAL-003 farkı sunucunun değeri (250.5); BAL-004 / BAL-006 "Hesaplanamıyor" ve eksik girdileri adlandırır; API okuması aynı |
| Bilinmeyen sonuç | İlk POST'un **gerçek** cevabı sunucudan alınır, tarayıcıya teslimi kesilir. Form donar. Aynı anahtarla yeniden gönderim replay olur. API: olgu **bir kez** kaydedilir (100, 200 değil) |
| Sürüm çakışması | Sayfa açıkken başka yazıcı gerçek API ile kaydeder. Form gönderimi 409, emir yeniden okunur. Yeni gönderim yeni anahtarla, yeni sürümde. API: 10 + 50 = 60 |
| Okuyucu | Denge ve kütle dengesi paneli okuma yetkisiyle görünür; form yok; hiç yazma isteği yok |
| Liste → emir (MFG-ORD) | `/uretim/emirler` gerçek GET ile okunur; satır sunucunun özetidir (durum, planlanan girdi); listede denge/fark kolonu ve uydurma etiket yok; satırdaki bağlantı **aynı** emri açar |
| Kapanış (MFG-ORD) | Kapatma yetkisi olan aktör: 1000 çıkış / 600 bildirim iken kapanış **reddedilir** ve ekran kararın gerekçesini (`manufacturing_unexplained_difference`) gösterir; emir API'de hâlâ `Open`. Kalan 400 gerçek API ile bildirilince kapanış kabul edilir; POST okunan sürümü ve kendi anahtarını taşır; API'de `Closed` ve `closedAtUtc` dolu. Bu aktör olgu formunu görmez |

- **Kimlik:** her vaka aktörünü istek başına başlık yeniden yazımıyla bağlar (web imajı kendi Development kullanıcısını gömer). Sayfanın aldığı her API cevabının yeniden yazılmış bir isteğe ait olduğu doğrulanır.
- **Mock yok:** yalnız kimlik yeniden yazımı ve bir teslim kesintisi var. Hiçbir durum ya da gövde yerelde üretilmez.

## Girdiler (varsayılan yok)
- BROWSER-CI sözleşmesi: `BROWSER_E2E_AUTHORIZED_BASE_URL`, `BROWSER_E2E_API_BASE_URL`, `BROWSER_E2E_ARTIFACTS_DIR`, `BROWSER_E2E_JUNIT_FILE`, `BROWSER_E2E_TESTED_COMMIT`.
- `BROWSER_E2E_MFG_SEED` (JSON): `recorderActor`, `readerActor`, `closerActor`, `companyId`, `workCenterId`, `orderId`, `rollId`.
  - a2 2. parçanın seed adımı bu değerleri sentetik test-DB grant'leriyle birlikte yazar.
  - recorder: beş Manufacturing izni, company-wide. reader: yalnız `manufacturing.orders.read`.
  - closer (MFG-ORD): `manufacturing.orders.read` + `manufacturing.orders.close`; olgu izni yok — kapatmak ayrı bir yetkidir.
  - Üç aktörün ayrı olması suite girdi doğrulamasında zorunludur.
- **Kabiliyet profili:** `normal` (MFG-UI-F).
  - Ekran normal runtime'da yayımlı: ilk kabul `mfg-staged` (D4) üzerindeydi (#323, run 35438064043).
  - Ekranı suite'in sentetik grant'leri açar, disposable zarf açmaz.

## Engeller (bu dosyanın sahibi dışında)
- MFG-ORD merkezi satırlar: `SUITE_IDENTITY_PERMISSIONS["mfg"]` altı isme (yeni `manufacturing.orders.close`) ve `SUITE_API_EXTENSIONS["mfg"]` üç aktöre çıkar; `manufacturing.orders.close` izninin merkezî kaydı; shell'in liste rotası. Hepsi CARİ'nin merkezi sırasında.
- a2 2. parça seed arayüzü; `mfg` suite kaydı (workflow case, `SUITES` / `SUITE_PROFILES` → `normal`); host'un sağlayıcı kaydı ve shell mount'u (#323 `SHARED_MOUNT_HANDOFF.md`). Hepsi CARİ'nin merkezi sırasında.
- Bunlar gelene kadar suite koşmaz; koşarsa girdileri reddeder. Yerel koşu yok (Docker WSL'de yok); ilk kanıt etiketli BROWSER-CI koşusudur.
