# W8 birim test kapanışı

Kaynak174/174 korunur. Tam ilk birim koşusu a8f4440ec88da785ee08ff5d2fa4500e72bfb4e8 üzerinde başlatıldı; her proje önce derlenir, sonra çalıştırılır. İlk native log/TRX/exit kayıtları depodışında korunur. Bu ara belge final exact-head kabul değildir.

## Accounting

İlk koşu679 test:672 başarılı,7 başarısız,0 skip, build0/test1.

- Beş hata, saldırgan şirket değişimini test eden fixture’ın gerçek değişmez CompanyContext.Set metodunu çağırıp testlenen servis çitine ulaşmadan atmasıdır. Mevcut testler korunur; yalnız adversarial test double kullanılarak servis katmanının ikinci CompanyId kontrolü sınanmaya devam eder. Decoder’ın yabancı şirket reddi ayrı gerçek foreign context ile sınanır. Production CompanyContext değişmez.
- FNS-D105 artık purchase-invoice kabulünü sağlar; unsupported-kind negatif testi gerçek tanımsız kaynak türüne taşındı. Desteklenen alış türü, farklı/satış anahtarını kabul etmez; mevcut anahtar reddi testi korunur.
- Muhasebe Shell negatif/route envanteri, ilk SMM ve geç alış maliyeti için mevcut altı staged route ile hizalandı; gerçek permission, grant veya availability değişmez. Beklenti listesi kaynak provider üzerinden tek tek doğrulandı.

Düzeltilmiş Accounting koşusu QUEUED. Test silme, skip, karantina veya doğrulama gevşetmesi yoktur.

## Counterparty ve Documents

- Counterparty ilk3 hata EF read-optimized modelde saklanmayan check-constraint metadata’sına erişimden gelir. Aynı model test dosyasındaki mevcut IDesignTimeModel örneği uygulanır; aynı üç testin bütün constraint/filter/index/append-only beklentileri korunur. DDL veya uygulama davranışı değişmez.
- Documents ilk1 hata byte[] içeren tuple’ın referans eşitliğinden gelir. İndirilen PDF baytları ve SHA256 ayrı yapısal assertion ile karşılaştırılır; belge/scanner kuralı ve her iki beklenen değer aynen korunur.
- Düzeltilmiş koşular QUEUED; ilk native hata kayıtları korunur.

## Identity

İlk4 hata: adversarial CompanyId değişimi için immutable gerçek context kullanılması; geçici parola verilişinde değişen security stamp/version’ın oturum fixture’ına taşınmaması; Identity Shell1.3’teki mevcut kullanıcı yönetimi rotasının eski1.2 beklentisinde olmaması; LocalCredentialCommand correlation metadata assertion’ının yanlışlıkla bootstrap entity’sinde bulunması.

Fixture yalnız testte mutable ICompanyContext kullanır. Tüketilmiş geçici parola testi mevcut stamp/version ile gerçek servis yoluna ulaşır. Bootstrap alanlarının zorunluluğu ayrıca korunur; correlation nullable/128 assertion’ı gerçek command entity’sinde tutulur. Shell yeni kullanıcı yönetimi route/permission kaydıyla tek tek hizalanır. Yetki/şifre politikası, production context veya grant değişmez. Düzeltilmiş koşu QUEUED.

## Organization ve Logistics

Organization takvim append-only negatif testi aynı guardın genişleyen hata metniyle hizalandı; modified/delete reddi aynen sınanır. Logistics SAT-D417 yeni plan üstü onay gerekliliği eski genel miktar aşımı kodundan ayrılır; terminal kayıt/replay/sürüm assertion’ları korunur. Shell1.6’daki iki yeni staged false amaç ve ayrı onay izni açık beklentilere eklendi; bütün eski route/control/permission ve availability beklentileri korunur. Düzeltilmiş koşular QUEUED.

## Product ve SecurityEvents

- Product SAT-D391/KIM-D45: kg/m şirket tablosundan gelir; profilde bulunmayabilir ve sac için de kayıtlı olabilir. Eski iki negatif varsayım yerine bu iki izinli kaynak sınırı ayrı pozitif örnekle korunur; negatif teori aynı sayıda gerçek sıfır wall/thickness ihlaliyle devam eder. Ağırlık uydurulmaz ve birim dönüşümü açılmaz. Similarity-key length prefix’inde coating yedi karakterdir; yanlış6 beklentisi7 oldu, değişen kontrollü değer assertion’ları aynen korundu.
- SecurityEvents tarihsel founder migration’ı sonraki iki-aşamalı kurtarma event genişletmesinden önceki kesin15 olay listesini taşır. Tarihsel Up tam listeyle, güncel model de founder olaylarını koruma şartıyla sınanır; Down/THROW ve diğer güncel model assertion’ları değişmez.
- Derleme/koşum QUEUED; ilk3+1 hata saklandı.

## Sales kaynak hizalaması ve ikinci koşu

Sales’te mevcut13 ilk hata için test fixture’ları: adversarial şirket değişimi; resmî fason VAT kaydının açık25+5 döviz tutarına ait200 TL karşılığını taşıması; legacy marj restore yolunun gerçek RestoreMarginCost üzerinden kurulması; replay actor’ının mevcut kullanıcıyla eşleşmesi; farklı kur günlü ikinci sevkin kendi irsaliye kaynağı; aynı başlık negatif testinden önce okunabilir boş future-allocation portu. Contract tarihçe alanı ve PinMarginCosts mutation-population beklentisi güncellendi; withdrawn durumda maliyet sabitlemenin reddi ayrıca sınanır. Nullable double teorisinin iki int girdisi double olarak açık yazıldı. İş tutarı/formülü/para politikası değişmez; fixture kaynak kanıtı tutarlılığı sağlanır.

İkinci sıralı denemede Finance, Counterparty, Documents, Organization testleri geçti. Accounting kalan1 COGS mapping route beklentisiyle hizalandı. Identity xUnit2029 assertion biçimi düzeltildi. Inventory’de InMemory bağımlılığı bu Unit projesinde yoktu; aynı dört Local izolasyon örneği zaten bu bağımlılığı taşıyan IntegrationTests projesine taşındı, yeni paket eklenmedi. Bunlar SQL kabulü değildir. İlk derleme hataları korunur.

Logistics’te gerçek bir terminal-record sınır hatası bulundu: plan üstü onay eksikliği prepare aşamasında atıldığından mutation içindeki mevcut terminal catch’e ulaşmıyordu. Aynı iki onay/çatışma409 kodu prepare yolunda da mevcut TerminalAsync üzerinden kayıt edilir; fresh authority fence ve payload-key kontrolü korunur. Mevcut negatif test ret, replay, correlation, sürüm ve yazılmayan load payını birlikte sınamaya devam eder. Bu backend delta recovery/fingerprint yenilemesi gerektirir. Son doğrulama QUEUED.

## İlk tam koşumun kesin sayısı

18 UnitTests projesi:13.168 test,13.091 başarılı,77 başarısız,0 skip.18 derlemenin tamamı exit0.6 proje ilk koşuda yeşil,12 proje başarısızdı; eski W5 hata sayısı güncel kabul olarak kullanılmaz. Son düzeltmelerin her biri ayrı native log/TRX/exit ile tekrar doğrulanır. Bu ilk koşunun kaynak tabanı a8f4440ec88da785ee08ff5d2fa4500e72bfb4e8; test kaynaklarındaki sonraki kapanış düzeltmeleri final kanıt değildir.
