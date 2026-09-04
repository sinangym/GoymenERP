# Göymen ERP — ChatGPT kaynak özeti

- Run-ID: `GOYMENERP-KNOWLEDGE-V4-01`
- Provider: `chatgpt`
- Source-ID: `chatgpt-goymenerp-otonom-uc-ajan-cari-teslimi-5864cddec534`
- Conversation-ID: `chatgpt-goymenerp-autonomous-cari-thread`
- Coverage: **partial**
- Repository evidence base: `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`
- Normatif durum: **NON_NORMATIVE_CANDIDATE**

Bu paket ham sohbet dökümü değildir. Aktif model bağlamında gerçekten görünür olan Göymen ERP
mesajlarından ve canlı canonical repository'de doğrulanabilen ilgili kanıtlardan atomik proje
bilgisi çıkarır. `Skipped N messages` olarak temsil edilen veya bu oturumda mount edilmeyen
eski içerik yeniden kurulmamıştır.

## Ana hedef ve çalışma modeli

Kullanıcı ERP'nin bütün modüllerinin uçtan uca çalışır hâle getirilmesini; yatay platform
hazırlığının kendi kendini besleyen bir döngüye dönüşmemesini; Codex, Claude ve ChatGPT'nin
aynı GitHub gerçeği üzerinde aynı dikey dilimin farklı katmanlarında paralel ilerlemesini
istemiştir. Codex backend/endpoint/gerçek-SQL/OpenAPI, Claude frontend/browser/a11y, ChatGPT
iş spec/acceptance/golden/backlog/hakemlik, ayrı coordinator ise merge rolündedir.

GitHub exact SHA, PR diff, CI ve repository kararları ortak gerçek kaynağıdır. Çalışma
farklı proje konuşmalarına bölünebilir; fakat dosya sahipliği ve exact-head review kuralları
aynı kalmalıdır. Davranış/güvenlik P0/P1 bulguları blocker'dır. Metin/test sayısı gibi P2
kanıt tutarsızlıkları tek başına yeni inceleme turu açmamalı, bir sonraki anlamlı incelemeye
eklenmelidir. READY K3 zincirleri önce, normal K2 merge'leri batch olarak ilerleyebilir;
Draft/NO_GO/P1-blocked K3 main'i süresiz kilitlememelidir.

## Ürün ve şirket kapsamı

Kullanıcı ürün kartının hukuki şirketler arasında ortak kimlik olmasını, şirket başına
ayrı ürün kartı açılmamasını belirtmiştir. Etiket/lot bazlı fiziksel kayıtlar şirket
ayrımı taşır ve varsayılan şirket siparişin açıldığı şirkettir.

GDC ve GSP gerçek şirketlerdir. GIN gerçek hukuki unvan ve gerçek VKN gelene kadar
placeholder ve resmî belge üretimine kapalı kalır. Gerçek, non-null VKN aktif/pasif bütün
şirketlerde grup çapında benzersiz olmalı ve SQL Server tarafında filtered unique index
ile uygulanmalıdır. Gerçek VKN değerleri privacy nedeniyle bu knowledge paketine
aktarılmamıştır.

Osmaniye Hol-0…7 ve Gaziantep başlangıçta Göymen iç lokasyonlarıdır. Baştuğ, Selimoğlu,
Alya, Şadiler, Tosçelik ve Doğruluk harici tedarikçi depolarıdır.

## Cari / Counterparty

Bir hukuki karşı taraf tek `Party` kimliğidir. Her Göymen şirketiyle ilişki ayrı
`CompanyPartyAccount` kaydıdır; müşteri, tedarikçi, fason ve hizmet rolleri aynı şirket
hesabındaki rol bitleriyle taşınır. Global Party aktifliği ile şirket hesabı aktifliği ayrı
iş olgularıdır.

`counterparty.accounts.create` yalnız `SYSTEM_ADMINISTRATOR` rolüne verilir; diğer roller
default-deny'dir. Şirkete bağlama işleminde kullanıcı gerekçesi zorunlu değildir. Hedef
şirket body/query'den seçilmez; shell tarafından doğrulanmış aktif `X-Company-Id`
bağlamından gelir. Kaynak şirket hesabının rolleri hedefe sessizce kopyalanmaz.

C-01A selected-company `SingleCompany` dilimidir; authorized multi-company görünüm ayrı
C-01B renderer/acceptance işidir. C-01'de `partyState` ve `accountState` ayrıdır,
çoklu rol seçiminde `roleMatch=any|all` kullanılabilir; count yalnız yetkili filtre
kapsamını ifade eden exact scope ile kabul edilir. Selected-company response aktif
CompanyContext ile eşleşen exactly-one account taşımalıdır.

İkinci sayfa `offline`, `network-error`, `500` veya `503` ile başarısızsa önceki doğrulanmış
satırlar korunur, yeni satır eklenmez ve kullanıcı kontrollü retry sunulur. `401`, `403`,
malformed veya tenant ihlali bu davranışın arkasına gizlenmez.

C-02A ilk create diliminde Party türü, hukuki unvan, opsiyonel ticari unvan, roller ve
salt-okunur aktif şirket bulunur. Cari kodunu server üretir. Mevcut command'da olmayan
VKN/TCKN, banka, adres, vergi/sicil, risk veya bakiye alanı UI tarafından uydurulmaz.
Idempotency anahtarı bir immutable submitted logical attempt snapshot'ına bağlıdır;
double-click, timeout ve unknown-outcome retry aynı key'i kullanır.

C-02 için canonical shell screen kimliğini `C-02`, `create-subset` değerini ayrı
`scopeVariant` olarak kullanma yönü konuşmada ChatGPT spec-owner önerisi olarak yer alır;
bu kaynak paketi bunu kullanıcı kararı diye promote etmez, `PROPOSED` olarak tutar.

C-03 detay modelinde `externalIntegrationCode` decoded business modele taşınmamalı;
tercih edilen nihai sınır dar server projection'dır. Int64 version değerleri kayıpsız
opaque decimal string olarak taşınmalıdır. Owner API'si olmayan bakiye/risk/stok/belge/AI
alanları uydurulmamalıdır.

## Shell güvenlik sınırı

Authenticated business-read `401/403` yalnız component-local hata olarak kalamaz. İstek
başlangıcındaki immutable actor/session/company/committed-revision snapshot'ı provider /
reducer seviyesinde authoritative current state ile karşılaştırılmalıdır.

Current business `401` terminal session invalidation, bir kez epoch ilerlemesi ve business
cache temizliği üretmeli; focus/reconnect/refetch eski veriyi diriltememelidir. Current
business `403` oturumu kapatmadan ilgili capability ve namespace'i fail-closed re-fence
etmeli; fresh capability olmadan route yeniden mount olmamalıdır.

Geç kalan Actor A `401` veya Company A `403` yeni Actor/Company B bağlamını etkileyemez.
Aynı güvenlik sonucu birden fazla observer tarafından görülse de effect exactly-once
uygulanmalıdır. Request abort güvenli bir sonuçtur fakat gerçekten teslim edilmiş stale
response'un merkezî fence tarafından reddedildiğini tek başına kanıtlamaz.

## Karar turu Q1–Q8

- Q1: GIN placeholder kalır; gerçek non-null VKN group-wide unique; gerçek GIN bilgileri açık.
- Q2: yaklaşık 5 milyar TL/yıl ve 30.000 TL/ton girdisi yaklaşık 166.667 ton/yıl planlama
  çıpası verir. Gerçek sipariş/müşteri/eşzamanlı kullanıcı hacmi açık kalır.
- Q3: ilk sürümde e-belge/Mikro/banka entegrasyonu yok; ileride sıra e-belge → Mikro
  mutabakat/onaylı fiş taslağı → banka hareketidir; serbest doğrudan DB yazımı yoktur.
- Q4: RPO 15 dakika / RTO 4 saat yalnız geçici varsayımdır, production hedefi değildir.
- Q5: yıllık dönem ve açık kayıtların aktif çalışma kümesinde kalması hedeflenir; çok-yıllı
  rapor/AI mümkün olmalıdır. Yasal retention/legal-hold ve teknik dönem temsili açıktır.
- Q6: her şirket ayrı logo/antet, şirket bazlı versiyonlu şablon ve Noto Sans teknik
  fallback kullanır; gerçek marka assetleri olmadan kurumsal PDF UAT bitmez.
- Q7: veri on-prem/kullanıcı tarafında, yerel-first AI/OCR temel UX; AI yetki, onay,
  deterministik formül veya iş doğrusu değildir. Bulut AI/KVKK seçimi açık ve default-deny.
- Q8: doğrulanmış satırları koru + manual continuation retry; otomatik resend yok.

## Kabul ve E2E

C-01 kabul matrisi gerçek capability→route→API→SQL zinciri, non-disclosure, stale şirket
switch, keyboard/focus/sort, responsive/reflow, accessibility, continuation, invalid cursor
ve Party/account durum ayrımını gerçek browser ortamında kanıtlamalıdır.

Yetkili ve yetkisiz browser profillerinin tek workflow run, tek exact SHA, tek fresh
SQL/API/fixture yığını üzerinde ayrı browser context/process ile çalışması istenmiştir.
Delivered-stale senaryoda CDP response-stage interception gerçek response body'sini
değiştirmeden yalnız teslim zamanını geciktirmelidir.

`C01-E2E-GOLDEN-V1` test-only fixture 54 Party ve 55 CompanyPartyAccount ile tanımlanmış,
29.817 byte canonical artifact ve SHA-256
`f23551944a65fe29681d768422b181dd73d68b1bdf8216e42bc643713e4d79d7`
olarak konuşmada gerçek yığında doğrulanmıştır.

PR #173 ile `tests/e2e/**` main'e girmiş olsa da post-merge audit nedeniyle bunun tek
başına browser acceptance sayılmaması gerektiği kayda geçirilmiştir. Final entegratör bu
tarihsel iddiayı güncel main ve browser workflow kanıtıyla yeniden sınıflandırmalıdır.

## Açık kararlar / riskler

Başlıca açık alanlar: GIN gerçek hukuki unvan/VKN; gerçek performans hacmi ve production
SLO; production SSO/MFA; gerçek sunucu/GPU/yedek envanteri; kesin RPO/RTO; entegrasyon
sağlayıcı/credential/sandbox; legal retention/legal-hold/partition-imha; gerçek
logo/antet/font assetleri; bulut AI sağlayıcı/KVKK/veri sınıfı aktivasyonu.

Current repo Hızlı Teslim v3 belgesinin başlığı `KULLANICI ONAYI BEKLİYOR` durumunu
taşırken kullanıcı bu konuşmada üç-ajan v3/otonomi modeline açık tam onay vermiştir.
Knowledge import bu normatif çelişkiyi sessizce çözmez.

## Güncel repository kanıtı

Export sırasında canonical repository `PRIVATE` olarak doğrulanmıştır. Exact main
`188c792900e35d3e2ed6a9ba4734dc9ad596fc97` seviyesindedir. `docs/knowledge/**`
altyapısı mevcuttur; `SOURCE_REGISTRY.json` hâlâ `collecting` durumunda ve kaynak listeleri
boştur. Bu kaynak PR'ı registry'yi değiştirmez; final Codex entegratörü tek-yazar olarak
registry'ye ekler.
