C-DESKTOP-010 — başlangıç ME → mevcut kanıt → yeni T senaryosu matrisi
Kaynaklar: C009 report/result; G-SUPPORT-004 response; 009 t-02..07 kodu ve F010 T ölçümleri. Eski 58 dosya sabit.

| ME | Mevcut T kanıtı | Yalnız yeni dal |
| --- | --- | --- |
| ME-002 | T03/T04 route.fulfill başarılı görünüyor fakat requestfailed=ERR_ABORTED; üretim callback teslimi kanıtlanmış değil | İptale duyarsız kontrollü taşıma; JSON tüketimi + Chromium precise-coverage ile gerçek command callback / security report çağrısı. Eski Link sonucu yeni şirkete, eski candidate 401 yeni actor/epoch'a etki etmez. |
| ME-003 recovery | T02 invalid_cursor/malformed sonrası doğrudan başarılı fresh dönüş | Recovery GET bekler; ilk sayfa 503 ile başarısız; yetki ve duyuru kapalı; kullanıcı fresh retry başarılı olunca açılır. İki farklı recovery yolu. |
| ME-003 context | T04 yalnız dirty Create; T07 revoke yeniden girişte/Create403 | Dirty Link Kal/Discard: cursor/selection/roles/key; oturum sürerken current candidate403; identity query üzerinden actor ve session epoch değişimi. |
| ME-004 retry | T05 yalnız Create unknown/retry | Link işlenmiş/işlenmemiş kontrollü timeout; tek mantıksal kayıt, aynı key/path/body, frozen alanlar ve yalnız kullanıcı retry. |
| ME-004 decoder | T06 Create foreign201/non201 ve candidate count | Link yanlış Party/missing/duplicate/unknown-role/string-version dalları; candidate source/version/paging bağları. Her fixture ayrı ölçülür, tablo testleri sayıyı şişirmez. |

T, gerçek Chromium içinde kontrollü API yanıtıdır; canlı HTTP/SQL, kalıcı replay, gerçek login veya ERP kabulü değildir.
Test-only entry, değişmeyen ShellProvider/queryClient test seam/router'ı kullanır. Pasif probe yalnız seçilmiş shell/cache alanlarını okur; aksiyonlar mevcut query'leri invalidate eder. Uygulama reducer outcome'u veya candidate cache verisi test tarafından üretilmez.
ME-005 EO/gerçek zoom, ME-001 canlı dallar, ME-007 CI/backend ve F011-ME-001 runtime açık kalır. Browser sınırında ölçülemeyen dal açık raporlanır.

## F-DESKTOP-015 eklemeleri — tanılanabilir koşum (F014-P2-001); F-DESKTOP-017 düzeltmeleri (C013-P2-001/002)

Her koşum kendi kimliğiyle çalışır; hiçbir koşum bir öncekinin çıktısını veya önbelleğini temizlemez, taşımaz veya üzerine yazmaz ve tarihsel `evidence/C-DESKTOP-010` klasörüne yazılmaz.

Sahiplik (`run-ownership.ts`, `run-identity.ts`): kimliği yalnız Playwright runner süreci, config yüklenirken kurar. Koşum dizini `<kanıt>/runs/<id>` ve Vite önbelleği tek seferlik, çakışmaya kapalı `mkdir` ile sahiplenilir (dizin varsa EEXIST = ret; içine dokunulmaz), soğuk başlangıç koşulu bir kez ölçülür ve `<koşum>/run-owner.json` yazılır. Worker'lar ve Vite web sunucusu `CARI010_RUN_CLAIM` ile aynı koşuma yalnız katılır: kaydı okur, iddiayı doğrular, kimliği kayıttan alır; hiçbir alt süreç yeniden sahiplik almaz veya değer ölçmez. Aynı `CARI010_RUN_ID` veya mevcut bir çıktı diziniyle ikinci başlatma (ardışık veya eşzamanlı) Vite/Playwright/ağ işi başlamadan, config yüklemesinde reddedilir. Önbellek yolu canonical (realpath) olarak doğrulanır: kanıt kökü ve `evidence` ağacı, depo çalışma ağacı ve tarihsel klasörler dışında olmalı ve var olmamalı; bu koşum oluşturur ve `c010-cache-owner.json` ile işaretler. Mevcut veya yabancı bir önbellek asla yeniden kullanılmaz, temizlenmez, taşınmaz. Bu kural nedeniyle her koşum soğuk başlar; ısınmış önbellekle "warm" koşum tasarım gereği desteklenmez.

| Değişken | Zorunlu | Anlam |
| --- | --- | --- |
| `CARI010_EVIDENCE_DIR` | evet | Bu görevin var olan kanıt kökü (mutlak yol). C-DESKTOP-010 klasörü reddedilir. |
| `CARI010_RUN_ID` | hayır | Koşum adı, `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`; yoksa UTC damgası. Var olan ad/dizin reddedilir, geçersiz ad sessizce düzeltilmez. |
| `CARI010_VITE_CACHE_DIR` | hayır | Vite bağımlılık önbelleği: mutlak, kanıt/depo dışında, henüz var olmayan bir yol (varsayılan: OS temp altında `goymen-cari-010-vite-cache/<id>`). |
| `CARI010_RUN_CLAIM` | iç | Runner'ın alt süreçlere aktardığı iddia kimliği; elle verilmez. Varsa süreç üye modundadır ve geçerli kayıt olmadan çalışmaz. |
| `CARI010_COLD_START` | iç | Runner'ın bir kez ölçtüğü değer (`1`/`0`); üyelerde kayıtla eşleşmesi doğrulanır, yeniden ölçülmez. |

Koşum dizini: `run-owner.json` (sahiplik kaydı: durum, iddia kimliği, soğuk başlangıç koşulu, önbellek yolu, başlatıcı PID), `tests.json` (safe-reporter; alan beyaz listesi), `diagnostics/warm-up.json` (global setup: kimlik alanları, Vite başlangıç/dinleme süreleri, ilk sayfa açılışı, optimizer bekleme süresi, ikinci geçiş, konsol/sayfa/istek hataları), `diagnostics/<spec>--<test>.json` (test başına: kimlik alanları; test anındaki önbellek metadata durumu ayrı alanda [`cacheMetadataPresentAtTest`]; sayfa açılış zamanları, konsol hata/uyarıları, pageerror, başarısız istekler [yöntem + yol + neden], gezinmeler, durum, hata mesajları, ek yolları), `T/*.json` (kontrollü istek/yanıt kayıtları: gövde yerine SHA), `service/ui-<pid>.json`, `playwright-output/` (yalnız başarısızlıkta ekran görüntüsü), `screens/` (zoom testi). Ortam değişkenleri, çerez, yetkilendirme başlıkları, depolama veya ham istek/yanıt gövdeleri hiçbir kayda yazılmaz.

Zaman aşımları ve warm-up: test 90 s ve expect 12 s (C010 ile aynı), `retries: 0`. webServer başlangıç zaman aşımı 180 s; C010/C011 adayında 120 s idi ve soğuk Vite bağımlılık optimizasyonu sunucu başlangıcına dahil olduğu için F-015'te artırıldı — başarısızlığı gizlemek için değil; maliyet warm-up kaydıyla ölçülür. Warm-up (global setup) link rotasını bir kez gerçekten açar, optimizer metadata'sını en çok 120 s bekler, ikinci geçişi ölçer ve `warm-up.json` yazar; warm-up başarısızsa kayıt yazılır ve koşum hiçbir test çalışmadan durur (globalSetup fırlatır). Playwright çıktı dizinini başlangıçta siler; koşum dizini bu koşum tarafından yeni sahiplenildiği için silinecek eski çıktı bulunmaz.

Tanılamadaki `net::ERR_NETWORK_CHANGED` gibi ağ hataları ve Windows ağ olayları yalnız zamansal ilişki gösterir; kesin kök neden sayılmaz. C010'un tarihsel ilk koşum arızasının nedeni açıktır.

Regresyon (geçici, göreve ait fixture'larda): `node --test tests/e2e/cari-acceptance-010/run-ownership.test.mjs` — aynı id'nin ardışık ve eşzamanlı sahiplenilmesinin reddi, işaretli eski çıktı/önbellek baytlarının değişmemesi, geçersiz önbellek konumunun iş başlamadan reddi, warm-up metadata'sı oluşsa da parent/worker kimliğinin sabit kalması.

Yeni spec'ler: `t-late-get-delivered.spec.ts` (ME-002: terminal 404/409 öncesi başlayan candidate GET'in teslim edilmesi — continuation ve ilk-sayfa refetch varyantları), `t-keyboard-link.spec.ts` (ME-005: rota açıldıktan sonra odak bir kez pointer ile `<body>`'ye sıfırlanır, sonrası yalnız klavye ile tam Link yolculuğu + hata odağı; pointer'sız açılış, ekran okuyucu konuşması ve görsel kontrast ölçülmez), `t-zoom-link.spec.ts` (ME-005: %200/%400 layout eşdeğeri; tarayıcı UI zoom kontrolü sürülmez).
