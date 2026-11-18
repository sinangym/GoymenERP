# W8 birim test kapanışı

Kaynak174/174 korunur. Tam ilk birim koşusu a8f4440ec88da785ee08ff5d2fa4500e72bfb4e8 üzerinde başlatıldı; her proje önce derlenir, sonra çalıştırılır. İlk native log/TRX/exit kayıtları depodışında korunur. Bu ara belge final exact-head kabul değildir.

## Accounting

İlk koşu679 test:672 başarılı,7 başarısız,0 skip, build0/test1.

- Beş hata, saldırgan şirket değişimini test eden fixture’ın gerçek değişmez CompanyContext.Set metodunu çağırıp testlenen servis çitine ulaşmadan atmasıdır. Mevcut testler korunur; yalnız adversarial test double kullanılarak servis katmanının ikinci CompanyId kontrolü sınanmaya devam eder. Decoder’ın yabancı şirket reddi ayrı gerçek foreign context ile sınanır. Production CompanyContext değişmez.
- FNS-D105 artık purchase-invoice kabulünü sağlar; unsupported-kind negatif testi gerçek tanımsız kaynak türüne taşındı. Desteklenen alış türü, farklı/satış anahtarını kabul etmez; mevcut anahtar reddi testi korunur.
- Muhasebe Shell negatif/route envanteri, ilk SMM ve geç alış maliyeti için mevcut altı staged route ile hizalandı; gerçek permission, grant veya availability değişmez. Beklenti listesi kaynak provider üzerinden tek tek doğrulandı.

Düzeltilmiş Accounting koşusu QUEUED. Test silme, skip, karantina veya doğrulama gevşetmesi yoktur.
