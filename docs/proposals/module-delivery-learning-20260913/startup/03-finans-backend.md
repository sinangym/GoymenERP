# finans-backend

Bu mesaj [ortak talimatla](COMMON.md) birlikte kullanılır; mevcut göreve tekrar başlangıç olarak gönderilmez.

ROL: Yeni modülün CODEX BACKEND yazarı; model tercihi Spark, başlangıç eforu medium. Kod/endpoint/gerçek-SQL/OpenAPI ve backend calculator senin katmanın, frontend/Business/merkezi durum dosyaları değil. Yeni karmaşık para/transaction/permission/cross-module tasarımını gerekli Astra değerlendirmesine çıkar; bütün basit işi güçlü modele tekrar yazdırma.

İlk olarak kendi worktree'nin main tabanını/temizliğini doğrula; eski yerel main'den açılmışsa mevcut değişiklikleri koruyarak origin/main'e güvenli normal hizala. Aynı merkezi checkout'a dönme. İlk turda modül kaynağı, reusable API/test ve eksik öncüllerin somut listesini kendi küçük başlangıç artefaktında çıkar ve koordinatöre gönder. Ortak dosya (Program/DI/solution/OpenAPI baseline/migration/CI) yazarlığı seri olarak coordinator tarafından bağlanmadan çakışma oluşturma. Finance veya Sales namespace/izin/route kesinliğini senin görev adından türetme.

Business kaynaklı kısa contract kesinleşince hedef testlerle API iskeleti ve Claude'a payload/hata örneklerini erken teslim et. Gerçek permission/para politikasını implementasyona karar gibi yerleştirme. İlk safhanda production veya API/SQL runtime başlatma; normal kod/test çalışması kendi yetkinde, gerçek ortam sonraki kabul planında kendi owner/port/fixture ile. Kullanıcının talimatı kapsam içi rutin teknik bugfixleri de kapsar; her P2'de yeniden işletme onayı sorma. İzin gerektiren araç ayrı sınırdır. Yeni bağımlılıkta kaynak incelemesini bitirip bir somut handoff bırak; eş görevin hazır olmasını kör poll etme.

FİNANS BAŞLANGICI
Kaynak: DEC-FIN-001…016, Master §7.10–7.11/9.2/10; W4-FINANS→W2-ACIKKALEM→W2-IRSALIYE+W1-SETTINGS. f6fa07d'de Finance/OpenItem/Settings Policy Registry runtime ve Finans API/feature acceptance yok; backlog acceptance=[] hazır kabul değildir. Yeni Finans çifti bu boşlukları görünür kılacak, gizlice bağımlılıkları done yapmayacak.

İlk aday, mevcut manuel finans kaynağı/para kaydı işinin karara bağlı en küçük kesitidir; yalnız Business kaynaklardan hazır alan/permission/iş etkisi belirleyebilirse uygulamaya geçer. Kaynak para olayı, açık kalem, çoktan çoğa dağıtım, nakit gerçekleşmesi ve ticari kapanış ayrıdır. Dağıtım ikinci kasa/banka/cari hareketi oluşturmaz. Dağıtılmamış=Kaynak Tutarı−Aktif Dağıtımlar; ters bağlantılar korunur. Vade/ödeme/valör/mutabakat tarihleri farklıdır. DBS yansıtması banka tahsilatı değildir.

OPEN-016 ilk sürümde manuel kayıt, banka/e-belge/muhasebe entegrasyonu yok; bunları bu işte başlatma. OPEN-009 işlem/rol/para/onay matrisi, OPEN-010 risk aktivasyonu, OPEN-012 kur zamanı/istisna sınırları seçilen akışı etkiliyorsa somut soru gerekir. Başlangıç finans tutar hard-gate'i olmaması yetkisiz yazma demek değildir. İleri dağıtım/açık kalem/irsaliye bağımlılığını ilk kayıt formuna gizlice taşıma ya da sahte fulfilled sayma. Dar bağımsız kesit ancak kanonik kaynaklarla Business ayrıştırması ve coordinator sırasıyla açılır.

CompanyId/currency/money precision/value-object ve permission kontratı baştan açık; UI hesaplayıcı uydurmaz, gerçek deterministic backend calculator ve golden kaynakları gerekir. FORMUL_KATALOGU metadata şablonu, SLT golden örneği Finans formül kanıtı değildir. Cari company-scoped idempotency/write-safety/replay testleri teknik örnektir; Product global GroupSharedCommandKey ve Cari optional-trade-roles-v1 başlığını Finans standardı yapma. OrganizationSettingsPage genel Policy Registry değildir.

İlk teslimin: mevcut finans kararları→alanlar→para etkisi→eksik karar/bağımlılık→owner tablosu, en küçük güvenli kesit önerisi, sentetik kabul örnekleri. Finans modelini sırf kredi ucuz diye tahminle uygulama; kritik yeni money/migration/permission/cross-module tasarımını Astra değerlendirmesine coordinator üzerinden taşı, aynı görevde kal.
