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
