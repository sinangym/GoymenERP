# Göymen ERP — Karar Paketi (Terminal İnsan Girişi)

`docs/governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md` bölüm 7 ve 18 uyarınca.

Bu dosyanın tek yazarı `CHATGPT-BUSINESS` derleyicisidir. Codex ve Claude aday girdiyi
Issue #66'ya `DECISION-PACK-CANDIDATE` olarak yazar; aynı dosyada paralel append yapmaz.
Derleyici duplicate `OPEN-*`, stable ID ve revision kontrolünden sonra ayrı PR hazırlar.

Dosya append-only'dir; girişler silinmez. Kapanan kayıt `ÇÖZÜLDÜ` durumu alır;
düzeltme yeni revision ve `SUPERSEDES` alanıyla yapılır. Bu dosya karar sicilinin yerine
geçmez ve açık işletme politikasını kendiliğinden kabul etmez.

## Format

```text
### ASSUMPTION-NNN  |  OPEN-XXX blocker  |  P0-BLOCKER
- Revision / status / supersedes:
- Alan:
- Nokta:
- Uygulanan varsayım:
- Dayanak:            (DEC-XXX | Master §N | "dış bilinmeyen, güvenli placeholder")
- Config / feature-flag:   (ad, mevcut değer, açık/kapalı)
- Yanlışsa etki:
- Sinan'dan gereken:
- Bekleyen dilim:     (BACKLOG id | "yok — flag açılınca canlı")
- Aday kaynağı:        (codex | claude | chatgpt) — <tarih> — <PR#>
- Kaynak aday:         (Issue #66 comment URL)
```

## Bilinen OPEN-* kilitleri — **TARİHSEL / SUPERSEDED**

> Bu tablo Faz 0 anlık görüntüsüdür ve aktif karar kuyruğu değildir.
> `DECISION-ROUND-2026-09-01-v1` tarafından supersede edilmiştir. Güncel durum
> ve aktif/kapalı ayrımı yalnız `docs/decisions/ACIK_KARARLAR.md` kanonik
> tablosundan okunur.

| OPEN | Konu | Geçici davranış | Kapanış kapısı |
|---|---|---|---|
| OPEN-001 | Hukuki şirket unvan / VKN / CompanyCode | seed placeholder (`TBD-GOY-01/02`) | şirket masterı canlı öncesi |
| OPEN-002 | Depo/lokasyon işleten şirket / sahiplik / harici taraf | organizasyon seed'de TBD | stok/mal kabul geliştirmesi öncesi |
| OPEN-003 | Fiziksel stok kabulünde kesin miktar kaynağı | tartılabilende kantar neti; yoksa yetkili kabul miktarı | mal kabul production öncesi |
| OPEN-004 | Kantar/yükleme tolerans eşikleri (ürün/işlem bazlı) | config tablosu, değer yok | kantar modülü UAT öncesi |
| OPEN-005 | Üretim kütle dengesi toleransları (hat/ürün) | profil yapısı, değer saha golden'dan | ilk üretim gerçekleştirme UAT öncesi |
| OPEN-006 | Kısmi hammadde asgari parti / onay eşikleri | politika tablosu boş varsayım | üretim başlatma UAT öncesi |
| OPEN-007 | Formül sahibi + yoğunluk/hız/kerf/kaplama/yuvarlama/tolerans | formula registry + golden şablon | ilgili formül production öncesi |
| OPEN-008 | Stok değerleme / üretim maliyet yöntemi | lot fiilî maliyeti ham veri; değerleme ertelendi | maliyet/muhasebe modülü UAT öncesi |
| OPEN-009 | Finansal onay tutar/rol/para eşikleri | dinamik politika motoru + boş şablon | finans yazma UAT öncesi |
| OPEN-010 | DBS/teminat haircut oranları + kullanılabilirlik | tür/banka/vade yapısı; oran 0 yalnız test verisi, production risk/teminat etkisi flag kapalı | risk motoru canlı öncesi |
| OPEN-011 | Minimum nakit tamponu + 13 haftalık plan kaynak önceliği | ayarlardan şirket bazlı | 13 haftalık plan UAT öncesi |
| OPEN-012 | Kur kaynağı alma saati / tatil / manuel istisna | önceki iş günü TCMB satış | fatura/kur motoru UAT öncesi |
| OPEN-013 | SSO sağlayıcısı / MFA / dış erişim politikası | OIDC soyutlaması + yerel admin bootstrap | production kimlik kapısı öncesi |
| OPEN-014 | Sunucu CPU/RAM/disk/RAID/GPU/OS/ağ/UPS/yedek envanteri | container-bağımsız geliştirme | dağıtım PoC öncesi |
| OPEN-015 | Üç yıllık veri hacmi / eşzamanlı kullanıcı / OCR hacmi | sentetik veri üreticisi parametreli | performans SLO kilitlenmeden |
| OPEN-016 | İlk entegrasyonların sırası + teknik erişim | adapter sınırları ayrık; gerçek entegrasyon yok | ilgili modül entegrasyon işi öncesi |
| OPEN-017 | Kesin RPO/RTO + felaket topolojisi | geçici RPO 15 dk / RTO 4 saat | production açılmadan |
| OPEN-018 | Belge türü bazlı saklama/imha + legal hold | policy tablosu + audit akışı | belge kasası production öncesi |
| OPEN-019 | PDF font ailesi + marka/antet dosyaları | Türkçe Unicode/gömülü font kabul testi; dosya pakette yok | PDF UAT öncesi |
| OPEN-020 | Bulut AI sağlayıcı / sözleşme / KVKK / izinli veri sınıfları | yerel-first, default-deny | bulut AI production çağrısı öncesi |
| OPEN-021 | Idempotency replay saklama / partition / imha | kayıt silinmez; ham anahtar tutulmaz; temizleme prod'da kapalı | production yük/retention kapısı öncesi |

## ASSUMPTION kayıtları

_(yalnız CHATGPT-BUSINESS derleyicisi, doğrulanmış Issue #66 adaylarını buraya ekler)_

## P0 blocker kayıtları

_(yalnız güvenle azaltılamayan P0 güvenlik/veri-bütünlüğü bulguları)_

## DECISION-ROUND-2026-09-01-v1 — KISMEN UZLAŞTIRILDI

- Revision: `v1`
- Status: **KISMEN UZLAŞTIRILDI — 2026-09-02**
- Taban: `main@80c567a63da8e059ce92aa1b23dad9f4568ad2ca`
- Karar sahibi: Sinan Göymen
- Derleyen: `CHATGPT-BUSINESS`
- Supersedes: bu dosyanın üstündeki tarihsel `Bilinen OPEN-* kilitleri` tablosunun terminal soru seçimi. Güncel açık-alt-karar gerçeği `docs/decisions/ACIK_KARARLAR.md` dosyasındadır.

Bu tur yeni feature spec değildir. Kararların kayıt altına alınması migration, production deployment, bulut aktarımı, purge veya dış entegrasyon credential yetkisi vermez.

### Q1 — GIN / VKN: `A`

- GIN gerçek unvan/VKN henüz yok; placeholder kalır.
- `IsPlaceholder=true`, `InvoiceCapable=false`; gerçek VKN gelmeden resmî belge kapalıdır.
- Eski `1111111111` gerçek VKN değildir ve implementation işinde kaldırılacaktır.
- Gerçek non-null VKN aktif/pasif bütün şirketlerde group-wide unique.
- SQL Server filtered unique index: `WHERE Vkn IS NOT NULL`.
- Kalan açık alt karar: `OPEN-001-GIN` gerçek unvan + gerçek VKN.
- [Issue #66 Q1 sahip açıklaması](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5506986884)
  benzersizliği açıkça onaylamış ve VKN'nin ilk kayıttan sonra değiştirilebilir
  olması gereksinimini eklemiştir.
- Placeholder'ın `NULL` veya geçici değer taşıması; değişiklik yetkisi/onayı/audit
  izi ve çakışmalı update davranışı `OPEN-030` kapsamında ayrıca kararlaştırılır.

### Q2 — Hacim: kısmi gerçek veri

- Yıllık ciro yaklaşık `5 milyar TL`.
- Ortalama ürün fiyatı yaklaşık `30.000 TL/ton`.
- Türetilmiş planlama çıpası yaklaşık `166.667 ton/yıl`.
- Gerçek ortalama sipariş tonajı bilinmediği için yalnız sentetik performans testi için `10–25 ton/sipariş` ve yaklaşık `6.700–16.700 sipariş/yıl` geçici aralığı kullanılır.
- Kalan: müşteri/cari sayısı, eşzamanlı kullanıcı, yoğun saat, belge/OCR hacmi ve production SLO.

### Q3 — Muhasebe/e-belge/banka entegrasyonları: ilk sürümde yok

İlk sürüm yalnız bu üç entegrasyon olmadan çalışır. Bu karar e-posta ve belge
paylaşımını kapsamaz; `DEC-DOC-006/007` yürürlükte kalır.

İleri öncelik:

1. resmî e-fatura/e-arşiv,
2. Mikro muhasebe mutabakatı ve kullanıcı onaylı fiş taslağı,
3. banka hareketleri.

Adapter yöntemi gerçek sağlayıcıya göre dosya, salt-okunur DB, manuel Excel/CSV veya yerel AI/OCR taslağı olabilir. Serbest doğrudan DB yazımı yoktur. Kalan: sağlayıcı, credential, sandbox ve exact erişim yöntemi.

### Q4 — RPO/RTO: `GECICI_VARSAYIM`

- Kullanıcı kesin değeri bilmediğini ve hedefin şirket/veri yoğunluğuna bağlı
  olduğunu belirtmiştir.
- `RPO=15 dakika`, `RTO=4 saat` yalnız geçici teknik hedeftir; kabul edilmiş
  production hedefi değildir.
- On-prem/hibrid ana çalışma ve tesis dışı şifreli yedek.
- Production kapısı: gerçek restore tatbikatı + DB/belge checksum mutabakatı.
- Gerçek yük/restore ölçümü ve ayrıca işletme onayıyla kilitlenir.

### Q5 — Yıllık dönem + retention ayrımı

- Kilitlenen gereksinim: yıllık yönetim; sonraki aktif çalışma kümesinde yalnız açık
  işlem/kalemlerin kullanılabilir kalması; yetkili seçili çok-yıllı rapor/AI analizi;
  gerçek silmeyi yalnız en yüksek yetkilinin gerçekleştirebilmesi.
- Kilitlenmeyen yöntem: açık ve kapanmış kayıtların dönemler arası davranışı;
  carry-forward/snapshot veya başka dönemler arası temsil;
  derived read model veya başka çok-yıllı okuma mimarisi; legal-hold, ikinci onay,
  audit ve otomatik purge davranışı.
- Bu yöntemler ile belge/audit/idempotency türü bazlı yasal süreler
  `OPEN-018/021` kapsamında açık ve production'da default-deny kalır.

### Q6 — PDF/logo/antet: `A`

- Her şirket ayrı logo/antet.
- Şirket bazlı, versiyonlu ve auditli şablon.
- Gömülü açık lisanslı `Noto Sans` teknik fallback.
- Gerçek asset kullanıcı tarafından sağlanmadan kurumsal PDF UAT tamamlanmaz.
- Kalan: asset dosyaları, varsa marka font lisansı ve exact onay permission/rolü.

### Q7 — AI: yerel-first ve temel UX

- Veri kullanıcı tarafında/on-prem kalır.
- AI + yerel OCR sürekli erişilebilir temel UX olarak tasarlanır.
- AI yetki, onay, formül veya iş doğrusu değildir; AI kesintisi normal ERP'yi bloke etmez.
- Yerel model/GPU/OCR PoC'si sunucu envanterine bağlıdır.
- Bulut AI sağlayıcı/KVKK/veri sınıfı ve ayrı aktivasyon olmadan default-deny kapalıdır.

### Q8 — C-01 continuation: `A`

- İlk doğrulanmış satırlar `offline`, `network-error`, `500`, `503` continuation hatasında kalır.
- Continuation-level açık retry vardır; otomatik resend yoktur.
- Aynı actor/session, CompanyContext/generation, query ve cursor snapshot'ı kullanılır.
- Hatalı sayfa append edilmez; başarılı retry bir kez append eder.
- `401`, `403`, tenant-binding ve malformed cevaplar maskelenmez.

## Bu karar turunda ele alınan başlıklardan gerçekten açık kalan kararlar

- `OPEN-001-GIN`: gerçek GIN unvanı ve VKN.
- `OPEN-014`: gerçek sunucu/GPU/UPS/ağ/yedek envanteri.
- `OPEN-015`: gerçek sipariş/müşteri/eşzamanlı kullanıcı/belge-OCR hacmi ve production SLO.
- `OPEN-016`: sağlayıcı/credential/sandbox ve exact entegrasyon yöntemi.
- `OPEN-017`: kesin RPO/RTO ve felaket topolojisi; geçici hedefler gerçek yük/restore ölçümüyle kilitlenecek.
- `OPEN-018/021`: yasal saklama süreleri, legal hold ve partition/imha matrisi.
- `OPEN-019`: gerçek logo/antet/font assetleri ve exact onay permission'ı.
- `OPEN-020`: yerel model/GPU PoC; bulut sağlayıcı/KVKK/veri sınıfı/aktivasyon.
- `OPEN-030`: VKN placeholder/değişiklik yaşam döngüsü, yetki/onay/audit ve çakışmalı update davranışı.

Kapanan alt bölümler yeniden `NEEDS-SINAN` olarak sorulmaz.


## DECISION-ROUND-2026-09-18-v1 — ÖNERİ / KULLANICI ONAYI BEKLİYOR

- Revision: `v1`; derleyen mevcut `CHATGPT-BUSINESS`.
- Status: **ÖNERİ — bu bölümde yeni işletme politikası kabul edilmedi.**
- Karar sahibi: Sinan Göymen. Kaynak aday: [Issue #66 / 5722291162](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5722291162).
- Kaynak tabanı: `main@eacbfe720aa426f4d6a71d5806603c461b30d9e2`; aynı Business PR #275 önceki head `97598785debb8372ecd47dcb9e1c683438ece160`.
- Supersedes: önceki round veya kabul edilmiş kararlar **yok**. Bu yeni aday turu yalnız gerçekten açık altları gruplar; tarihsel kararlar silinmez veya değiştirilmez.
- `Ö1–Ö8` soru gruplarıdır; yeni `OPEN-*`, görev veya kabul kimliği değildir. Onaylanmış K-1(a)/maliyet ertelemesi önceki tarihli kayıtta ve PR #275'te korunur.
- Uygulanan yeni varsayım: **yok**. Yeni config, gerçek profil/seed, rol/grant, migration, saklama veya production aktivasyonu yapılmadı.
- Yerel uygulanan iyileştirme ayrı kanıt: [5722240264](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5722240264). Eski stop/respawn/model-policy launcher'ları karantinada; iş kuralı veya repo governance değişikliği değil.

### Aynı kaynak adayının öneri içeriği

Sinan, ödeme/Chromium adımlarına kadar diğer yavaşlıkların giderilmesini ve kendi iş anlayışı/çelik sektörü/kullanıcı kolaylığıyla uyumlu karar önerileri istedi. Yerel iyileştirme kanıtı 5722240264; aşağıdakiler **yeni işletme onayı veya seed/grant izni değildir**. Aşağıdaki içerik bu kanonik pakete append-only olarak alındı; ikinci master/kabul listesi oluşturulmadı.

### Tekrar sorulmayacaklar
K-1(a) kabul; ayrıntılı maliyet/OPEN-008 ana sistemden sonra; OPEN-002 depolar, OPEN-003 manuel kantar-akışı, OPEN-006 başlangıçta asgari parti kısıtı yok; tek Party/şirket hesabı; kritik Cari masterda **hazırlayandan farklı bir yetkilinin tek onayı**, rutin iletişimde ayrı onay yok; ilk sürüm banka/Mikro/e-belge canlı entegrasyonu yok; yerel-first AI. Kod/gerçek veri/asset eksikliği bunların insan kararını yeniden açmaz. Tolerans sayıları ve tüm profiller bu kısmi kararlardan türetilmez.

### Önce insan cevabı gerektiren dar paket — öneriler
**Ö1 / mevcut ilk aile kartı 5654539215:** İlk tam kullanıcı doğrulaması için GDC'de **bant/rulo, kg→kg** yolunu öneriyorum; GSP aynı ürün kimliğini ayrı şirket hesabı/stok sınırlarıyla kullanır. Kalınlık/en pozitif mm, kalite ve kaplama durumu kontrollü referans zorunlu; kaplamasız açık değer, bilinmeyen null. Renk yalnız boyalı ailede anlamlı; rulo kartına sabit boy veya fiilî heat/MTC doldurma zorunluluğu yok. İlk gerçek ürün ve sözlükler mevcut yetkili kayıt/etiketten okunup kaynakla önerilir; S235JR, 40x40 ya da herhangi bir geçmiş örnek otomatik onaylı master değildir. İlk aile diğerlerini kapsam dışına çıkarmaz.

**Ö2 / aynı kart, precision/effective-date:** Önerilen kayıt hassasiyeti kg ve nominal mm için en fazla **3 ondalık**, adet tam sayı, TRY/USD/EUR nihai tutar **2 ondalık**, birim fiyat **en fazla 6 ondalık**; bu değerler sektör standardı değil kullanıcı onayına aday. Ekran gereksiz sıfırları göstermesin, kaynak kantar yalnız tam kg veriyorsa sahte küsurat üretmesin. Fazla basamak sessiz kesilmesin; ticari son yuvarlama ayrı onaylı politika olarak gösterilsin. İlk UAT profili GDC/TRY; geçerlilik açık onay tarihinden, bitiş boşsa **açıkça süresiz** anlamına gelsin ve yeni sürüm eski işlemi değiştirmesin. UUID/version/lookup kodlarını kullanıcıdan istemeyin. Finans UAT için 2026 açık dönem önerisi ayrı ve yalnız izole UAT'tır; canlı geçmiş dönemleri açma izni değildir.

**Ö3 / ilk aile traceability:** Lot/etiket/soy ağacı minimumu zaten zorunlu. Heat/coil/MTC şartı lot ve alış/müşteri kalite şartına göre; zorunlu belge eksikse malın fiziksel gelişi 'belge/kalite bekliyor' olarak kaydedilebilir fakat uygun üretim/sevke serbest bırakılmaz. Şart olmayan alımlarda mevcut üretici kimliğini koru, eksik kimliği uydurma. Product kartının aktivasyonu fiilî lotun henüz gelmemiş sertifikasına bağımlı olmasın. MTC yükümlülüğünü genel yönetici override'ıyla hukuki/sertifikalı şartlardan kurtarma yok.

**Ö4 / OPEN-009, ilk Finance yazma UAT:** Mevcut manuel kayıt ilkesi ve başlangıçta tutara bağlı hard-gate olmaması korunarak, zaten gerçekleşmiş banka tahsilatını dar Finance operatörü kaydetsin; basit her kayda iki kişi şartı koymayalım. Gerçek para çıkışı talimatı, banka hesabı değişikliği, kesin kayıt tersleme, dönem açma ve risk istisnası için hazırlayandan farklı tek yetkili onayı öneriyorum. Teknik rol kodu yetki mühendisi işidir; kullanıcı kimin hangi şirkette maker/checker olacağını seçer. Gerçek kullanıcı ataması ayrıca; izole test aktörü production yetkisi değildir. Parasal eşik talebi varsa para/şirket/işlem bazlı tablo, tek genel TL rakamı yok.

**Ö5 / Finance tarih kapsamı + OPEN-012:** İşlem/belge, kayıt zamanı ve valör ayrı. Yalnız yetkili açık döneme kayıt; kapalı dönem silinmez/açılmaz, gerekçeli düzeltme onay kuyruğu. İlk dev/UAT'de yalnız test yılı/şirketi açık, canlı dönem onayı ayrı. Mevcut önceki iş günü TCMB satış başlangıç kararı korunur; hafta sonu/tatil/yarım günde son yayımlanmış uygun önceki iş günü kaydı, kur tarihi/kaynağı/yönü görünür. Manuel kur istisnası gerekçe+yetki+audit. Kaynak tutar ile değerleme/raporlama kurunu aynı amaç sanma. İşlem onaylanınca yeni kur geçmişi değiştirmez. Kesin otomatik alma saati teknik uyarlanabilir zamanlama; ticari kur sözleşmesini veya yasal kuru bu rapor seçmez.

**Ö6 / CARI-DEC-004 ve #232 §14.6.6 açık ortak-scope:** Kritik ortak Party değişikliklerini dar **grup-master onay yetkilisi** onaylasın; her bağlı şirketten ayrı ayrı onay toplamayalım. Mevcut maker≠checker tek onay kuralı korunur. Şirket-özel iletişim yalnız kendi scope'unda. Grup master onayı bütün şirket bakiyelerini okuma yetkisi vermez; yetkisiz şirket adı/sayısı da sızmaz. Otomatik hesap B yerine hedef şirkette açık 'Bu şirkette hesabını oluştur' eylemi öneriyorum; gerekli mevcut create+görünür kaynak izinleri yoksa hedef hesap yok. Sadece GET/arama/seçim account yaratmaz. Bu öneri yeni rol veya otomatik grant değildir.

**Ö7 / OPEN-004/005/007:** Toleransları kantar farkı, ticari fazla/eksik teslim ve proses kütle dengesi olarak ayrı tutalım; aynı yüzde hepsine geçmesin. Mevcut %1/%2/%3 ve ±%2 rakamlarını sektör normu diye onaylamayalım. Öneri: süreç sahibi son tamamlanmış işlerden izlenebilir örneklerle hat/ürün bazlı sayı önerir; kullanıcı tek tabloda onaylar. Tolerans içi rutin işlem ek onay istemez; istisna tek açıklama/onay kuyruğu. Plan override'ı yalnız yetkili ve o planın snapshot'ı, masterı değiştirmez. Sayısal değer gelene kadar generic motor/test sentetik fixture'la hazırlanabilir, gerçek üretim serbestliği verilmez. Üretim sorumlusu reçete/hat değerini, mühendislik formülü/golden'ı sağlar; LLM rakam uydurmaz.

**Ö8 / OPEN-030 ve GIN:** Placeholder VKN için sahte 111... yerine **NULL**, `IsPlaceholder=true` ve `InvoiceCapable=false` öneriyorum. Gerçek şirket VKN'si grup çapında benzersiz; değişimde belge/fark/gerekçe ve farklı yetkilinin tek onayı, geçmiş fatura snapshot'ı korunur. Yeni bir tüzel kişiliğe dönüşümü kimlik düzeltmesiyle yapmayalım. GIN gerçek unvan/VKN kullanıcı/şirket belgesinden gelmeden resmî işleme açılmaz; GDC/GSP geliştirmesini durdurmaz.

### Canlı öncesi ve ertelenenler — bu gece genel engel değil
OPEN-010: nominal teminat ile gerçekten kullanılabilir limiti ayır; doğrulanmış tutar yoksa risk azaltımını otomatik yapma, 0 haircut'ı ölçülmüş tam güvence sanma (risk ekranı aktivasyonu öncesi kullanıcı onayı).
OPEN-013/014: production MFA/VPN/SSO ve gerçek sunucu/GPU/UPS envanteri; geliştirme makinesi production envanteri yerine geçmez.
OPEN-015/017: gerçek kullanıcı/iş hacmi ölçümü ve restore provası; mevcut RPO15dk/RTO4sa yalnız teknik hedef, garanti değil.
OPEN-018/021: yıl bazlı okuma/açık kayıtlar korunur; yasal süreler muhasebe/hukuk sahibiyle doğrulanmadan otomatik purge yok; teknik partition kararını kullanıcıya yazılım sorusu yapmayın.
OPEN-019: gerçek logo/antet ve yayımlayacak kişi; eksik asset bütün ERP kodunu durdurmaz.
OPEN-020: yerel AI/OCR gerçek donanım PoC'si; bulut veri aktarımı halen ayrı karar.
Ertelenmiş: OPEN-008 ayrıntılı maliyet, OPEN-011 ileri nakit/13 hafta, canlı banka/Mikro/e-belge adapter seçimi OPEN-016, özel kur-kâr koruma/15gün çevrim ve sonraki HR/Bakım/CAPA/CRM/Sabit Kıymet modülleri. Temel belge/kalite/para/izlenebilirlik çekirdekte kalır.

### Teknik işlerin insan sorusu yapılmaması
Teknik UUID, permission adı, DI, gerçek sağlayıcının kodu, kayıt/query ve lookup ekranı, fixture/izole test actor'ı, kaynak işlem idempotency ve negatif yetki testleri uygulayıcı işi. Henüz production profili onaylanmadığı için bütün CRUD/servis kodunu durdurmak gerekmez; ancak üretime hazır/gerçek kullanıcı kabulü diye raporlanamaz. Verilmiş K-1'i tekrar sormayın. Onaylı öncül-main/CI kapıları aynen; sentetik veri gerçek veri diye sunulmaz.

Sektör dayanağı: GS1 Global Traceability Standard, üretim girdisi–çıktısı ve batch/instance kimliklerini korur; SSAB ürün karekod örneği kimliği ürün bilgisi/sertifikaya bağlar. Bunlar izlenebilirliğe dayanak, genel % tolerans ya da bütün mallara MTC zorunluluğu değildir. TCMB'nin resmî 'Gösterge Niteliğindeki Kurlar' açıklaması iş günü/15:30 ve tatil/yarım gün ayrımını destekler; tarafların/yasanın uygulanacak kurunu buradan otomatik seçmeyin.

**Bu sekiz öneriyi uygulama için ONAYLANDI saymayın.** Kullanıcı karar verince aynı kaydın ilgili satırı karara bağlanır; mevcut kapalı kararlar ve ertelenmiş işlerin onayı yeniden sorulmaz.

### Önerilerin kabul sınırı ve kullanıcıya cevap kolaylığı

Ö1 ve Ö2 ilk çalışır seçim/sipariş yolunu açacak öncelikli pakettir. Ö4–Ö5 ilk Finance yazma kabulü öncesi; Ö6 ortak kritik Cari masterı öncesi; Ö3/Ö7 ilgili mal kabul/üretim/sevk kullanımı öncesi; Ö8 ilgili şirket kimlik işlemi öncesi karara bağlanır. Hepsini aynı anda cevaplamamak bütün ERP geliştirmesini durdurma gerekçesi değildir.

Sayısal hassasiyet önerisi bir ölçümün fiziksel doğruluğunu iddia etmez. Kullanıcının ölçü aleti/kantar çözünürlüğü ve mevcut gerçek kayıtları doğrulanır. Birim fiyatın 6 basamak desteği, bütün değerleri 6 basamağa zorunlu çevirmek değildir. **Ticari son yuvarlama yöntemi ve hangi aşamada uygulandığı ayrıca ilgili sözleşmeye bağlanmadan** '2 ondalık' onayı keyfî round-to-even/away-from-zero tercihi veya vergi hesabı onayı sayılmaz. Test yılı/profili production açık dönem değildir.

Onaylayan kişi ataması, şirket kapsamı ve vekili gerçek kullanıcı kararıdır; bunun UUID/permission kodu eşlemesi teknik ekip işidir. Ortak Party onayı bütün şirketlerin finansal bilgilerini okuma yetkisi yaratmaz. Gerçek finansal olayı kaydetme ile henüz yapılmamış ödemeyi yetkilendirme iki ayrı kullanım durumudur.

Belgeli/sertifikalı sevk şartının ihlali 'bir yönetici onayladı' diyerek aşılmaz. Gerçek malın fiziksel gelişi kayıt altına alınır; kalite/evrak serbestliği ayrı durumdur. Süreç sahibi numune/önceki iş kaydı üzerinden tolerans önerir; sektörel kaynak genel bir yüzde belirlemiş gibi yazılmaz.

İlk gerçek ürünün ticari adı, kalite/kaplama ve nominal ölçüleri için mevcut kullanıcı belgesi/etiketi veya yetkili mevcut kart kaynağı kullanılmalıdır. Başka konuşmadaki araştırma örneği, geçici fixture veya rulo stok miktarı onaylı ürün masterı değildir. Kaynaklara erişilemiyorsa yalnız o gerçek veri eksikliği kullanıcıya sorulur.

### Birincil dış kaynaklar — kapsamları sınırlı

- [GS1 Global Traceability Standard](https://www.gs1.org/standards/gs1-global-traceability-standard/current-standard): lot/instance ve dönüşüm girdisi–çıktısı izlenebilirliği; bu ERP için zorunlu GTIN edinimi veya sabit tolerans kararı değildir.
- [SSAB Ürün Karekod Markalaması](https://www.ssab.com/tr-tr/destek/product-data-matrix-marking): ürün kimliğinin teknik bilgi/sertifikaya bağlantısı ve telefonla okutma; bütün çelik türlerinde her MTC'nin zorunlu olduğu iddiası değildir.
- [TCMB Gösterge Niteliğindeki Kurlar](https://www.tcmb.gov.tr/wps/wcm/connect/TR/TCMB%2BTR/Main%2BMenu/Temel%2BFaaliyetler/Doviz%2BEfektif/Doviz%2Bve%2BEfektif%2BPiyasalari/Gosterge%2BNiteligindeki%2BKurlar): yayımlama takvimi ve kurların gösterge niteliği; ticari anlaşma/yasal değerleme kurunu otomatik belirlemez.

Mevcut kaynak bağları: `ACIK_KARARLAR.md`; `SINAN_KARARLARI_2026-08-31.md`; `CARI_CORE_USER_DECISIONS_v1.md` CARI-DEC-004; #232 `adb9842fee07908c6d690c58057b97fa81fd8bfb` §14.6 ortak master kapsamı; ilk aile kartı #254 / 5654539215; K-1(a) / 5719251344. Eski `CARI_ACCOUNT_CREATE_PERMISSION_DECISION_v1.md` tarihsel seçenek tablosu tek başına güncel grant kararı kabul edilmedi.
