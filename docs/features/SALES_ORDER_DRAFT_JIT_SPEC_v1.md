# W2-SATIS — satış siparişi taslağı, liste ve detay: ilk JIT altı

Owner: **CHATGPT-BUSINESS** · 13 Eylül 2026 · v1 · **İŞ/KABUL TASARIMI; API ve gerçek kabul NOT_RUN**.
Kaynak kesiti: `main@f6fa07d026403649d6c815b84fd850e7ceeaf092`. Uygulama: yeni Sipariş Backend + Sipariş Claude; oturum/dosya tahsisi aynı CODEX-ORCHESTRATOR `01a08cbe-8af5-73d1-bf7e-39353ffc82da` tarafından bağlanır. Bu belge atama makbuzu, yeni rol/grant veya production onayı değildir.

## 1. Kaynak ve tek kullanıcı sonucu

Son kullanıcı kararı > [DEC-SAL-001…008][decisions] > [Master §7.2][master] > bu uygulama ayrıntısı. [10 Eylül Q01/Q02/F01 ve Talep/Sipariş kararı][latest], #229 birleşmiş sayılmadan kullanılır. [Mevcut W2-SATIS][backlog] kapsamı ve W1-CARI + W1-URUN kenarları silinmez; öncüllerin dar geliştirme kapanışı bütün modül kabulü değildir.

**İlk pozitif yol:** açık A şirketinde mevcut Cari hesabını ve Product sahibince satışa kullanılabilir bulunan malı seç → miktar/birim/termin ve mevcut ticari bilgileri gir → **Sipariş / Taslak** kaydet → listede bul → detayını aç ve tekrar oku. Taslak kaydı, müşteri veya iç onay, sevk taahhüdü, rezerv/tahsis, stok, irsaliye, fatura, açık kalem veya tahsilat oluşturmaz.

Talep/Sipariş bir belge türü ayrımıdır; Taslak/Onaylı bir yaşam döngüsü eksenidir. Aynı ekran ailesinde tutulur; ayrı ön sipariş menüsü yoktur. Bu ilk teslimin pozitif yazması mal satış siparişi taslağıdır. Talep dönüşümü, hizmet/fason satırları, onay, kritik revizyon uygulama, kaynak tahsisi, teslim/iptal/kalan kapatma ve faturalama mevcut hedefte kalır; ilk alt bunları tamamlamaz.

## 2. Alanlar ve durumlar

Aşağıdaki adlar **mantıksal contract** girdisidir; mevcut endpoint/DTO iddiası değildir. Yeni alanların teknik uzunluk/collection/page limitlerini Backend OpenAPI ile erken pinler; sessiz kesme yoktur. Para/ölçü hassasiyeti ise onaylı profile bağlıdır, teknik limit diye uydurulmaz.

| Alan | İlk altın kuralı |
|---|---|
| Şirket, actor, kayıt zamanı | Şirket doğrulanmış request context'inden; actor/UTC kayıt zamanı sunucudan. Body CompanyId/actor ile hedef değiştirilemez. Grup görünümü yazma hedefi değildir. |
| id, sipariş no, version, revizyon | Sunucu üretir. Şirket-kapsamlı numaralandırma; görünür no teknik UUID değildir. Rev.0 korunur. Concurrency version ile ticari revizyon ayrı; version kayıpsız string olarak sözleşmeye bağlanır. |
| belge türü / yaşam döngüsü | İlk komut açık Sipariş niyeti taşır, server Taslak üretir. Client Approved, müşteri-onaylı, sevk/fatura/finans-kapalı durumlarını yazamaz. |
| PartyId / şirket hesabı | Mevcut yetkili hukuki taraf + A'ya ait hesap ayrı bağlanır. `roles=0` geçerlidir; Customer flag aranmaz. Pasif taraf/hesap yeni işlem seçicisine açılmaz, eski belgedeki snapshot silinmez. Hesap yoksa otomatik B/link yoktur. |
| iş tarihi / termin | İş tarihi date-only `YYYY-MM-DD`, UTC kayıt anından farklı. Üst termin, satır termini ve miktarlı termin dilimleri ayrı; bilinmeyen termin null, bugüne/vadeye dönüştürülmez. Girilmiş tarih gerçek takvim tarihi olmalı. |
| satır kimliği / ProductId | Bu ilk pozitif örnekte en az bir mal satırı; sabit satır kimliği retry'da korunur. Kimlik üretiminin client/server tek sahibi Backend tarafından pinlenir; Cari child UUID kuralı otomatik taşınmaz. |
| miktar / sipariş birimi | Miktar pozitif, sonlu, kayıpsız decimal; UnitId gerçek owner referansı. İlk golden kg→kg kullanır. Paket, adet, metre veya fiyat birimi kg varsayılmaz; dönüşüm yoksa sonuç üretilemez. |
| fiyat, iskonto, para, fiyat birimi | Para birimi ilk kayıtta zorunludur; sipariş birimi ile fiyat birimi ayrıdır. Fiyat verilirse geçerli fiyat birimi de zorunludur. Draft'ta henüz kesinleşmemiş fiyat/koşul açık eksikliktir; null bedelsiz/0 fiyat değildir. Verilmiş değer, para profili ve yetkiyle doğrulanır. Bilinmeyen vergi/kur/iskonto sırası için toplam veya marj üretilmez. Fiyatlandırma tamamlanmadan onay/kesin kullanım yoktur. |
| teslim adresi / muhatap / sorumlu | Verilirse yetkili owner kaydına bağlı; firma adresi ile A hesabının adresi ayrılır. Kullanılan kayıt/sürüm snapshot'ı Sales'te korunur; master sonradan değişince eski sipariş sessiz yenilenmez. Bilinmeyen adres/sorumlu otomatik seçilmez. |
| vade / kaynak stratejisi / paket | Vade modeli, termin ve fiilî ödeme günü farklıdır. Verilmiş paket/koşul sürümü korunur. İlk kayıtta bilinmeyen null; fiziksel lot, mal sahibi veya tahsis bu alanlardan türetilmez. |
| iç açıklama | Opsiyonel metin; null ile eksik alan anlamı DTO'da açık. Açıklama izin, onay veya ürün/birim yerine geçmez. |

Create'te version istenmez; yanıt version'ı zorunludur. Sonraki update'te gönderilmeyen alan değişmez, null yalnız nullable alanı temizler; koleksiyon eksikliği silme değildir. Bu ilk create/list/detail teslimi update/revision motorunun uygulandığı anlamına gelmez. DEC-SAL-002'deki kritik değişiklikler uygulanınca eski revizyon tutulur ve yeni onay gerekir; Q02 uyarınca bekleyen yeni sürüm eski onaylı sürümü geçersiz kılmış gibi gösterilmez. Termin-only değişiklikte DEC-SAL-003 ayrı kalır. Onay yetkili kişi kendi siparişini onaylayabilir; erişim-yetkisi maker-checker kuralı buraya kopyalanmaz.

## 3. Komut, şirket ve referans sınırı

Backend aynı küçük pakette CreateDraft, authorized List/Detail ve gereken seçim projection'larının route/DTO/error/version/izin pinini verir. Mantıksal örnek; UUID yer tutucular gerçek fixture değildir:

```json
{"kind":"Order","partyId":"<yetkili-Party>","companyAccountId":"<A-hesabı>","businessDate":"2026-09-13","currencyId":"<onaylı-para>","lines":[{"productId":"<satışa-uygun-Product>","quantity":"1000","orderUnitId":"<owner-kg>","requestedDeliveryDate":"2026-09-20"}]}
```

Kaydetme öncesinde özet, açık şirket ve **henüz fiyatlandırılmamış/onaysız** durum görülür. Kesin success; tek sipariş/Rev.0/satır snapshot'ları + audit + kalıcı komut sonucu aynı transaction sınırında. GET/selection/preview iş kaydı yazmaz. Sonuç gövdeli olacaksa client onu validate eder; 204 seçilirse gövde uydurmaz, fresh GET kullanır. Exact status/error map Backend'indir; platform403'te zorunlu olmayan `code` beklenmez.

[GetAuthorizedPartyAsync][party-read] ve [CounterpartyDetailView][party-model] yetkili okuma sağlar; Party'nin görünmesi uygun A hesabı, aktiflik, adres yetkisi veya Sales yazma yetkisi yerine geçmez. Sales, Cari tablosuna yazmaz. Product için [mevcut Draft/reference sözleşmesi][product] tüketilir: **Draft + Sellable=true veya Complete etiketi tek başına usable değildir**. Seçici Product sahibinin lifecycle, rol, gerekli referans/birim ve geçerli sürüm sonucunu kullanır; sunucu kayıttan önce yeniden doğrular. Eksik katalog boş/başarılı doğrulama değildir.

Sales liste/detail/draft-create, fiyat-tutar okuma ve gelecekte approve/revise ayrı şirket/işlem eylemleridir. İzin adları ve mevcut rol eşlemeleri mevcut Identity sahibiyle pinlenir; [bugünkü genel izin kataloğunda][permissions] Sales eylemi yoktur. Yokluk default-deny; Product `SYSTEM_ADMINISTRATOR`/global grant'i Sales yetkisi olmaz. İlk sentetik test aktörü ayrı yetkileri açık taşır; bu gerçek kullanıcı ataması değildir. Operasyon seçicisi için dar Product read yetkisi gerektiğinde owner contract kabul edilir; şirket adminine global master izni verilerek aşılmaz.

Unknown kaydetmede aynı key/path/payload/actor/şirket/girişim korunur; timeout/hatalı2xx yeni sipariş denemesi başlatmaz. Yeniden denemede güncel yetki kontrolü vardır. Farklı payload ile aynı key çatışmadır. Şirket değişiminde dirty uyarı/kal-bırak çalışır, geç response B'ye kayıt/toast taşımamalı. 401/403/404, gerçek boş, yükleniyor, ilk/sonraki sayfa hatası ayrı; taslak ve hata özeti korunur. Cari ACK veya cursor formatı yeni modüle kör kopyalanmaz. Masaüstünde tab sırası, görünür odak, hata ilişkisi ve listeye dönüş ilk kabul içindedir; mobil toplu kabul sonraki aşamadır.

## 4. Gerçek dependency ve tek kısa sözleşme uzlaştırması

| En küçük eksik/yeniden kullanılacak bağ | Sahip / kapanış ölçütü |
|---|---|
| Party/account okuma ve işlemde aktiflik/sürüm kontrolü | Mevcut Cari Backend; A/roles0 pozitif, B/pasif/eksik hesap negatif; otomatik account açılmaz. Cari L03/L04 yeniden geliştirilmez. |
| Kullanılabilir Product + gerekli kg/satış birimi ve referanslar | Mevcut Product Backend + ilgili Settings/Identity sahibi. Draft CRUD kapanışı activation/reference runtime teslimi değildir. İlk gerçek satır kabulü owner'ın gerçek usable projection/fixture'ını bekler; yalnız Sellable veya test SQL UPDATE ile Active yapma kabul değildir. |
| Şirket/dizin ve Sales eylemleri | Mevcut Organization/Identity + Sipariş Backend. Her write tek şirket, exact permission registry/denetim ve read-contract; yeni şirket/depo işi yok. |
| Currency/quantity profili; varsa fiyat/kur/koşul | Settings + fiyat/Finance sahibi. Decimal sınırı gerçek kaynaktan; ilk taslak fiyat motorunu taklit etmez. Farklı birim/FX/tolerans golden'ı yalnız ilgili sonraki hesapta gerekir. |
| Sales aggregate/persistence/API ve ekran | Yeni Sipariş Backend / Sipariş Claude. Teknik modeller, collection limitleri, success/error örneği ve permission mapping tek Backend kaynağı; Claude aynı örnekleri decoder/formda kullanır. ENV veya birbirlerinin bitişi contract hazırlığını bekletmez. Shared dosyalara tek writer'ı coordinator atar. |

**Bir uzlaştırma:** Backend tek request/list/detail/success/error örnek seti + referans/version/transaction ve izin sınırını; Claude required/null gösterimi, aynı-key recovery ve draft liste dönüşündeki UX farklarını birlikte döndürür. Rutin route/sayfalama/teknik uzunluk seçimi yeni Sinan sorusu değildir. Aynı Business her kod değişikliğini yeniden onaylayan kişi yapılmaz. Eksik öncüller yalnız kullanan altı sınırlar; model/DTO/örnek UI ve test tasarımı bağımsızdır. Tam W1 veya W2 bağımlılıkları DONE yapılmaz.

## 5. Mevcut kabul bağı ve golden örnekleri

Aşağıdakilerin tamamı **TASARLANDI / NOT_RUN**. A/B, aktif Party/A-account roles0 ve owner'ın satışa kullanılabilir P/kg referansı kontrollü fixture koşullarıdır; gerçek master veya grant değildir. [AT kataloğu][acceptance] değiştirilmez.

| Bağ / kullanıcı adımı | Beklenen kayıt ve oluşmayacak etki | Kanıt |
|---|---|---|
| AT-001/003/025: A'da 1000kg sipariş Draft kaydet, listeden aç/yenile | Tek A siparişi+Rev.0+satır, audit/terminal; snapshot aynı. B listesinde yok. Cari/ürün masterı, tahsis/irsaliye/fatura/para değişmez. | HTTP + gerçek SQL + aynı browser yolculuğu |
| AT-013/014/015/016 sınırı: pasif/başka şirket/eksik hesap; Draft veya eksik-birim Product; rol0 aktif hesap | Geçersiz/yetkisiz seçim ret, kısmi kayıt0. Roles0 tek başına ret nedeni değil. Referans yokluğunda sahte UnitId/usable veya otomatik link yok. | Owner contract + SQL negatif; component seçim/hata |
| AT-003/005/006: cevap teslimini kaybettir, aynı key tekrar; farklı payload veya yetki kaybı | Bir sipariş/numara, replay'de ikinci etki0; farklı girişim güvenli çatışma. Audit/terminal fault tüm yazmayı geri alır. | Gerçek HTTP/SQL; temsilî controlled delivery loss browser |
| AT-006/008: kirli form A→B; geç A cevabı; server validation hatasını düzelt | Taslak B'ye taşınmaz, otomatik POST0; hata özeti/alan ilişkisi ve girdiler korunur; açık düzeltmeyle tamamlanır. | Pending-Promise component + temsilî browser/context |
| AT-024 hazırlığı: 1000kg, termin dilimleri 400+600; 400+700 dene | İlk toplam1000; ikinci toplam1100 sipariş miktarını aşamaz. Kısmi termin girişinde henüz planlanmayan200 gibi kalan ayrı, yapılmış teslim değil. Fiilî teslim yok; kısmi teslim AT-024 henüz PASS değil. | Deterministik miktar testi; farklı birim dönüşümü bu golden değil |
| AT-021/022/023/007 açık hedef | İlk Draft onay/risk eşiğini geçmiş sayılmaz. Onaylı sipariş revizyonu, iptal-etki ve kısmi teslim sonraki owner akışlarıdır; Rev.0/snapshot silinmez. | Bu altların gerçek kabulü henüz yok |

Finansal toplam golden'ı fiyat birimi/para/yuvarlama/vergi contract'ı olmadan çalışmış sayılamaz. Bilinmeyen fiyat/risk 0 değildir. Sadece taslak olduğundan onaylı-sipariş risk bileşenine katılmaz; risk motoru uygulanmış sayılmaz.

**Sonraki tek iş:** Bu taslağın fiyat/koşul/ürün uygunluğu tamamlanmış iç onaya gönderme öncülleri; otomatik tahsis veya faturalama değil. Bu teslimde yeni işletme kararı seçilmedi; eksik gerçek para/izin politikası kendi ilgili kapısında kalır.

## 6. Öğrenmenin kalıcı uygulama bağı

[LESSONS #253/1bb9fed][lessons] öneridir, kural değil. Request optional ≠ response optional → Cari `mutationContract.test.ts`/`detailContract.test.ts` → Sales decoder örnekleri. Unknown + geciken recheck → `CariCreatePage.test.tsx` pending-abandon testleri → Sales aynı girişim; Cari hook'u import edilmez. Modal/company çatışması → [T09][t09] → gerçek kal/bırak ve odak; force-click yok. Discovery/typecheck erken → `Product` test projesinin CI discovery dersi + browser `--list` → test kodunun gerçekten derlenip keşfedildiği gösterilir. Source/component/controlled browser/real SQL/actual browser ayrı etiket; aynı miktar/tarih veya source SHA tek başına actual PASS değildir.

[decisions]: https://github.com/sinangym/GoymenERP/blob/f6fa07d026403649d6c815b84fd850e7ceeaf092/docs/decisions/KARAR_SICILI.md#L59
[master]: https://github.com/sinangym/GoymenERP/blob/f6fa07d026403649d6c815b84fd850e7ceeaf092/docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md#L269
[latest]: https://github.com/sinangym/GoymenERP/blob/e1fd69da69df36a72e559ea07163fb3f359b3c61/docs/decisions/SINAN_KARARLARI_2026-09-10.md
[backlog]: ../backlog/DELIVERY_BACKLOG_v1.json
[acceptance]: ../acceptance/ACCEPTANCE_MATRIX.csv
[party-read]: ../../src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Application/Contracts/ICounterpartyReadService.cs
[party-model]: ../../src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Application/Contracts/CounterpartyModels.cs
[product]: ../../src/backend/Modules/Product/README.md
[permissions]: ../../src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs
[lessons]: https://github.com/sinangym/GoymenERP/blob/1bb9feda3e937bbd615daef41267d7afbad66c54/docs/proposals/module-delivery-learning-20260913/LESSONS.md
[t09]: ../../tests/e2e/cari-acceptance-009/specs/t-09-account-status-switch-layering.spec.ts
