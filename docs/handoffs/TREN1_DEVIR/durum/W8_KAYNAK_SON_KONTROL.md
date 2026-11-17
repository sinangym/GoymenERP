# W8 son kaynak kontrolü

**172/174 (%98,9) kaynak DONE; iki açık dilim.** W8 son pakettir. Yayımlanan tren135/174; son pin/hafif kontroller ve fast-forward henüz yapılmadı.

## Yeni kapanış ve düzeltme

- KIM-S19: gerçek finans matris/kasa/banka/K6 aileleri, numaralama ve açılış tüketicileri bağlı. `e7029e6bc` kazanan komutun son okumasından sonra fresh yetki/şirket/aktör çitini ekler; iki gerçek SQL regresyon kaynağı vardır. Son dar inceleme somut yeni P0/P1/P2 bulmadı; K3 onayı değildir.
- SAT-SC34: `dfc1f3195` gerçek onaylı iadeyi açık miktara, `4e0d582e1` gerçek aktif-kesinleşmiş fatura payını mevcut miktar hesaplayıcısına bağlar. DTO/strict decoder/ekran/HTTP/SQL/birim kaynakları mevcut; belirsiz kaynak sıfır yapılmaz. Dış resmî belge sağlayıcısı yapılandırılmamış ve ayrı kabul kapısıdır.
- STK-S3 yeniden PARTIAL: özgün fason akış ve kullanılmayan ayırma bırakma kodu tamamdır; son 14 kayıtlı karar takibi denetimi **SAT-D417 plan üstü sevk uyarısı/onayı** kaynağının olmadığını buldu. Önceki dört dilim kapanışındaki bu enclosing durum düzeltilmiştir; açık iş yüzdeye gizlenmez.

## İki açık kaynak

1. **STK-S3 / SAT-D417:** mevcut gerçek normal/fason sevk miktarı LoadedNotShipped ile sınırlı; SAT-D367 açık planı bu Issued talimatın payını içeriyor. Geçerli kayıtta istenen miktar kendi açık planını aşamıyor. Erişilemez warning veya sahte onay yazılmadı, fiziksel sınır/constraint gevşetilmedi. Kullanıcıya ve [Issue1189/6963231247](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6963231247) kaydına talimat revizyonu/ayrı istisna ve onay yetkilisi soruldu; öneri yanıt sayılmaz.
2. **STK-SC36-a:** `baf1d0add` gerçek atıflı pay iptali/ayrı onay/two-table EF merkezde. Maliyet kaynağı iptalden sonra gelirse gerçek tarihsel finalization/cancel kanıtı, mevcut cancellation receipt ve yeni gerçek ekonomik pay sahibinin korunması son dar owner çalışmasıdır. Kanıtsız veya henüz kesinleşmemiş kaynak başarı yapılmaz; başka faturanın fişi terslenmez.

## Birleşik kaynak derlemesi

Donuk head `15a6342904570eb9e93cda2cdb0acac4ce10cb69`:

| Proje | Native exit | Süre |
|---|---:|---|
| API | 0 | 2:45,15 |
| Settings.UnitTests | 0 | 3,42sn |
| Finance.UnitTests | 0 | 7,09sn |
| Inventory.UnitTests | 0 | 6,85sn |
| Sales.UnitTests | 0 | 13,33sn |
| Accounting.UnitTests | 0 | 4,72sn |
| IntegrationTests | 0 | 50,36sn |
| TypeScript `tsc -b` | 0 | native çıkış kaydı korundu |

Backend 0 uyarı/hata; her süreç öncesi en az2GiB boş fiziksel bellek ölçüldü. Tam log/native exit depodışında korundu; ilk hata kayıtları silinmedi. Bu **derleme** kanıtıdır. Unit/gerçek SQL/HTTP/Vitest/browser/CI **çalıştırılmadı**. DB apply, grant/rol, OPEN kapatma, Finance STOP veya production değişikliği yoktur.

## Eski takip notları

`KALAN_PLAN.json` içindeki 14 kayıtlı karar takibi gerçek kaynakla karşılaştırıldı. Bildirim deposu/pasifleştirme `adee18054`, kg ağırlıklı ilk kur `4bd80d9dc`, Product/hesap eşlemesi `c51d83d86`/`e13015a66`, numaralama ve kapalı dönem kredi düzeltmesi kaynakları eski PLANNED/PARTIAL notlarından ileridedir. URT-D109 maliyet kuralı değildir: boya iade üst sınırı emir toplamındadır (`f0efbaebb` ve golden kaynağı). Kredi taslak/insan aktivasyonu, yalnız izin kayıtları ve runtime eksikleri ayrı korunur; yeni işletme kararı veya OPEN kapanışı türetilmedi.

## SAT-D417 doğrudan kullanıcı cevabı

Kullanıcı: “100 kg kalıp 20 kg için ayrı istisna kaydedilsin, satış müdürü,”. [6963301286](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6963301286) bu doğrudan oturum cevabının açıkça işaretlenmiş ajan aktarımıdır. Talimat miktarı korunacak, ek miktar ayrı kalıcı istisna ve satış müdürü onayıyla yürütülecek. Aynı STK-S3 içinde kaynak çalışması başlatıldı; fiziksel yükleme/sevk ve diğer gerçek owner limitleri korunur. Yeni soru/OPEN kapanışı/grant/production kararı türetilmez. Mevcut172/174 kaynak sayısı değişmedi; iki kalan iş de uygulama aşamasındadır.
