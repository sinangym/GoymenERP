# W8 SQL ön analizi — 19 Kasım 2026

Kaynak: yayımlı b5373e67e00bc9445b32341213b323b14480eaad; hazırlanan takip a022623ad0b8f5e44786fb3509f45bf777359342. Sabit174 kaynak dilimi korunur, yeni dalga yok. Bu kaynak/korunmuş TRX analizidir; yeni native build veya test başlatılmadı.

## Kesin bulgular ve sınırlar

| Yüzey | Kanıt / neden | İşlem |
|---|---|---|
| Yerel A grain Fact'i | TRX line241: purchase.receipt/PURCHASE.RECEIPT aynı sayılmadı. Canlı SQL collation Turkish_CI_AS; DB eşitliği README'de otoritedir. | 1ead6b21f sentetik source.test/SOURCE.TEST/Source.Test kullanır. Whole/line, duplicate, FK ve unique beklentileri korunur; üretim sorgusu/collation/migration değişmez. Native NOT_RUN. |
| Yerel B üç ProductPurchaseSelection Fact'i | Üç stack aynı DisposeAsync line234/237. Factory.DatabaseConnectionString yerel AttachDBFilename içerir; cleanup yalnız InitialCatalog=master yapar. SQL mevcut dosyayı master olarak attach etmeye çalışır. | Yeni ürün politikası gerekmez. Cleanup master bağlantısında AttachDBFilename temizlenmeli. ProductPriceList aynı onarımı zaten uygular. Kaynak taramasında factory bağlantısından master'a geçip bu alanı temizlemeyen tek doğrudan kullanım bu sınıfta bulundu. Cleanup hatası asıl test hatasını maskeleyebilir; testin iş kısmının PASS olduğu iddia edilmez. Henüz uygulanmadı/test edilmedi. |
| GitHub B kasa yarışı |627 sonuçta1 FAIL. Onay treasury SERIALIZABLE, ret dışarıda; bariyer sonrası ret veya onay1205 kaybedeni olabilir. Ret save helper bu hata yolunu kapsamıyordu. | ada125245 ret de mevcut treasury rollback sınırına alındı. Aynı Fact iki test oturumu önceliğiyle her iki aktörü birer kez gerçek1205 kaybedeni yapacak; tek kazanan/typed Unavailable/version4/commands4/audit1 ve native1205 sayısı sıkıdır. Test oturumları pool'a dönmez. Native NOT_RUN; yeni politika veya retry yok. |
| GitHub A transfer yarışı |639 sonuçta1 FAIL. Guard optimistic/ilk-insert çakışması InventoryStoreException.ConcurrentChange üretir; dispatch/accept doğrudan guard çağrıları staging'deki transfer hata eşlemesinin dışındaydı. | b0b9f07b6 yalnız bu kodu mevcut Transfer409.ConcurrentChange'e çevirir. Timeout/deadlock/diğer istisnalar geniş biçimde eşlenmez. Kaynak boşluğu kesindir; CI'deki failing alt adım olduğu özel TRX/yeniden üretim olmadan kesinleştirilemez. Native NOT_RUN. |
| A süre sınırı |76 tamamlanmış batch/608 sonuç:607 PASS1 FAIL; global10800 saniyede durdu,31 sonuç kanıtı yok.76 native batch toplam10660,1s; test sonuçlarının zaman aralıklarının birleşimi10306,0s; dışarıda354,1s. | Süre artırılmaz. İşlem dışı overhead yaklaşık%3,3; batch birleştirmek tek başına yeterli bir çözüm olarak sunulmaz. Test içi DB hazırlığı/migration/seed/yürütme süreleri ayrılmadan snapshot/paylaşılan DB/paralellik/sunucu ayarı değiştirilmez. |
| Süre hata metni | run_test_checked gerçek deadline=min(2400, kalan global bütçe), fakat hata metni daima2400 der. A sonuç kaydı10800,26s. | Yanlış teşhis riskidir. Sonraki dar runner düzeltmesi actual effective budget/global vs process deadline ayrımını göstermeli;40/180 kapıları korunmalı. Henüz uygulanmadı/test edilmedi. |

## Test başlatmadan önce yapılacaklar

1. Yeni native hedef kuyruğu HELD_BEFORE_NATIVE_START_FOR_SOURCE_ANALYSIS: yalnız bekleyen operatör durduruldu; hiçbir hedef testi başlamadı. Mevcut tam b537 SQL kendi kaynaklarıyla sürüyor; testi/kanıtı silmek, skip veya PASS üretmek yok.
2. Mevcut üç hazırlanmış düzeltmenin farkını ve cleanup düzeltmesinin etki alanını incele. Gerçek karar/sözleşme korunur. Production/instance/global collation ve workflow değişmez.
3. Her doğrulanmış kök neden için tek hedefli kabul seç: grain1, kasa1 içinde iki kontrollü1205, transfer1 ve ProductPurchaseSelection3. Fixture/durable state/CompanyId/audit/command count ve SDK/TRX exact kimlikleri şarttır.
4. Kaynak/derleme temiz olduktan sonra ilk yeni koşum yalnız bu küçük hedef kümesi olmalı. Kaynak incelemesi SQL başarısı değildir. Yeni hedef koşumu bu analiz sırasında başlatılmadı.
5. Hedefler geçince ilgili modül birimleri/mimari/contract kontrolleri ve eksik31 testin ayrı tanısı; sonra son güncel head'de tam CI. Eski head PASS'leri farklı head'de tek tam kabul gibi toplanmaz.
6. Mevcut180 dakika sınırında tam yerel A'nın bitmesi garanti değil. Test içi maliyet azaltımı ölçülmeden kapsam küçültme/kapı gevşetme veya daha yüksek paralellik yapılmaz. GitHub SQL16 ve yerel SQL17 sonuçları ayrı kalır.
7. Son fresh tam CI ve iki bağımsız tam exact-head K3 review olmadan #1201 ready/merge olmaz. OPEN/grant/rol/Critical/production/Finance STOP değişmez.
