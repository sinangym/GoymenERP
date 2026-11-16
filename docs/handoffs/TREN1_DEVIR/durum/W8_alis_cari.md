# TREN-1 W8 — Alış / Cari

Bu son paket mevcut 174 dilim kapsamındadır; yeni dalga veya iş politikası açmaz.
ALC-S6 **PARTIAL**: tek-kabul ve çok-kabul owner zincirleri yazıldı. Batch genel
satır başına tek net etki ve sipariş başına tek sürüm uygular. Bildirim alıcı owner
bağlantısı, gerçek host sağlayıcıları ve tümleşik kabul tamamlanmadan DONE sayılmaz.

## Kabul düzeltmesi ve iade sahibi

- ALS-D86/D91/D92/D93, ALIS_F017 bölüm3.7/3.8: özgün brüt kabul payı değişmez;
  geri alma ve varsa yeni kabul tek net işlem/sürümde kaydedilir. Net sıfır düzeltme
  kapalı/iptal siparişi geçici olarak canlandırmaz. Normal yeni kabul kapısı korunur.
- Cancelled ve API-11 kapalı satır yalnız pozitif net açılan pay kadar canlanır.
  Eski iptal/kapanış payı ayrıca düşülmez; iki kat arz oluşmaz. İlk ve sonraki iptaller,
  kapanış bilgileri ve yeniden iptalin yalnız yeni kalanı tüketmesi korunur.
- Yenisi beklenmeyen tedarikçi iadesi kabulü azaltır ve kesin iade payına eklenir;
  açık tedarik oluşturmaz. Gelecek tedarik, açık alış, Satış bağlantılı tedarik ve termin
  okuma kaynakları bu payı da kapsar. Teslim edilmiş kesin iade payının termini açılmaz.
- Borrowed transaction participant commit yapmaz. Şirket, özgün kabul/satır ilişkisi,
  expected version, satır kilidi, ortak idempotency denemesi ve immutable sonuç korunur.
  Aynı anahtar daha sonraki iptal/sürümden etkilenmeden ilk yanıtı döner.
- Yeni Audit kaydı Audit sahibinin transaction portundan aynı işlemde yazılır;
  yeni bir modüller arası Audit tablosu yazarı veya filtre bypass'ı eklenmedi.
- ALC-S5 bağı: iade önerisinin sipariş sürümü/geri alma miktarı kalıcıdır. Yeni iade
  denemeleri ilk yanıt görüntüsünü saklar; sonradan reddedilen önerinin ilk anahtarına
  ilk Pending yanıtı döner. Eski NULL görüntülü denemelerin açık legacy fallback'i
  raporda kalır; eski kayıtlar tahmini yanıtla geriye dönük doldurulmaz.

## Migration ve K3 notları

- EF10.0.8 üretimi `20261116213347_AddPurchaseReceiptAdjustments`: immutable düzeltme
  ve iptal geçmişi, şirket/sipariş/satır/kabul birleşik ilişkisi, yeni miktar payları ve
  kısıtlar. Down ilk koruması **51949**. Eski Cancelled header gerçekleri geçmişe
  aktarılır; bu veri mutasyonu gerekçeli **K3 review bekliyor** olarak taşınır.
- EF10.0.8 üretimi `20261116215806_AddPurchaseSupplierReturnResponseSnapshots`:
  ilk iade yanıtı görüntüsü ve JSON kısıtı; Down ilk koruması **51953**.
- Yeni ham SQL/bypass guard girdileri yalnız yeşil sonuç için izin listesine eklenmedi.
  Ortak bağlantı/transaction katılımı ve iki Down/veri koruma SQL'i K3'te incelenir.
- EF10.0.8 üretimi `20261116222957_AddPurchaseReceiptAdjustmentBatches`: batch başlığı,
  değişmez detay/deneme bağlantıları, tek sürüm indeksleri ve outbox rowversion.
  Down ilk koruması **51956**, batch veya herhangi bir outbox cursor geçmişi varken
  veri/tekrar yayım kaybını reddeder. Boş depoda generated geri alma korunur.
- Purchasing outbox sabit `MIN_ACTIVE_ROWVERSION` sorgusuyla yalnız commit edilmiş
  güvenli sırayı okur; şirket filtresi korunur. Erken commit olan sonraki mesajın
  eski açık transaction mesajını atlatmaması için gerçek SQL yarış kaynağı eklendi.
  Ham ufuk okuması gerekçeli **K3 review bekliyor**; dispatcher etkinleştirilmedi.
- Grant/rol, production, OPEN durumları, Finance STOP ve altyapı değişmedi.

## Doğrulama

API startup derlemesi ve EF üretimleri exit0. Purchasing.UnitTests derlemesi exit0,
sıfır hata/uyarı. Api.IntegrationTests derlemesi de exit0, sıfır hata/uyarı.
Kaynaklar: net sıfır/pozitif canlanma, kapanış/iptal geçmişi, kesin iade,
değişmez replay, aynı transaction Audit rollback, shared terminal deneme, model
append-only/şirket ilişkisi, gerçek SQL Down51949/51953 ve ilk Pending replay.

Tam test, gerçek SQL/HTTP, Vitest, browser ve CI koşumları **NOT_RUN**; mevcut
kırmızıların geçtiği veya bu kaynakların kabul aldığı iddia edilmez.

## Çoklu kabul düzeltmesi kaynak checkpoint'i

- ALS-D86/D91/D92/D93: bütün özgün paylar, kimlikler ve sonuç miktarları önce
  doğrulanır. Bir erken geri alma ile sonraki yeniden kabul geçici canlanma üretmez.
  Net sıfır batch Cancelled durumunu ve sıfır tahsis edilebilir miktarı korur.
- Tüm batch sonucu, sıralı detaylar ve genel satır toplamları değişmez saklanır;
  sonraki tekil düzeltme/version artışından sonra replay ilk sonucu döndürür.
  Eksik başlık/çocuk geçmişi başarıya çevrilmez. Bütün owner yazıları ve Audit aynı
  ödünç transaction'a katılır; participant commit yapmaz.
- Yalnız gerçekten canlanan genel satır için `purchasing.order-line.revived.v1`
  outbox olayı kaydedilir. Satış ilişkileri ve alış oluşturan/onaylayan alıcıları
  bildirimin owner portlarına ayrıca bağlanacaktır; kişi varsayımı yapılmaz.
- Son batch kaynaklarında Purchasing.UnitTests ve Api.IntegrationTests (API dahil)
  derlemeleri exit 0, sıfır hata/uyarı. Unit/model/SQL kaynakları yazıldı;
  test koşumu yapılmadı. Pin/ilişkisel envanter son entegrasyonda üretilecektir.
