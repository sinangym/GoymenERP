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

## Bilinen OPEN-* kilitleri (Faz 0 karar sicilinden — ajanlar buraya dilim bağlar)

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

## DECISION-ROUND-2026-09-01-v1 — ÇÖZÜLDÜ

- Revision: `v1`
- Status: **ÇÖZÜLDÜ — 2026-09-01**
- Taban: `main@e64c2ad7cf3f557541e57f5f6b3d3e1dea36123d`
- Karar sahibi: Sinan Göymen
- Derleyen: `CHATGPT-BUSINESS`
- Supersedes: bu dosyanın üstündeki tarihsel `Bilinen OPEN-* kilitleri` tablosunun terminal soru seçimi. Güncel açık-alt-karar gerçeği `docs/decisions/ACIK_KARARLAR.md` dosyasındadır.

Bu tur yeni feature spec değildir. Kararların kayıt altına alınması migration, production deployment, bulut aktarımı, purge veya dış entegrasyon credential yetkisi vermez.

### Q1 — GIN / VKN: `A`

- GIN gerçek unvan/VKN henüz yok; placeholder kalır.
- `Vkn=NULL`, `IsPlaceholder=true`, `InvoiceCapable=false`.
- Eski `1111111111` kaldırılacak.
- Gerçek non-null VKN aktif/pasif bütün şirketlerde group-wide unique.
- SQL Server filtered unique index: `WHERE Vkn IS NOT NULL`.
- Kalan açık alt karar: `OPEN-001-GIN` gerçek unvan + gerçek VKN.

### Q2 — Hacim: kısmi gerçek veri

- Yıllık ciro yaklaşık `5 milyar TL`.
- Ortalama ürün fiyatı yaklaşık `30.000 TL/ton`.
- Türetilmiş planlama çıpası yaklaşık `166.667 ton/yıl`.
- Gerçek ortalama sipariş tonajı bilinmediği için yalnız sentetik performans testi için `10–25 ton/sipariş` ve yaklaşık `6.700–16.700 sipariş/yıl` geçici aralığı kullanılır.
- Kalan: müşteri/cari sayısı, eşzamanlı kullanıcı, yoğun saat, belge/OCR hacmi ve production SLO.

### Q3 — Entegrasyon: ilk sürümde yok

İleri öncelik:

1. resmî e-fatura/e-arşiv,
2. Mikro muhasebe mutabakatı ve kullanıcı onaylı fiş taslağı,
3. banka hareketleri.

Adapter yöntemi gerçek sağlayıcıya göre dosya, salt-okunur DB, manuel Excel/CSV veya yerel AI/OCR taslağı olabilir. Serbest doğrudan DB yazımı yoktur. Kalan: sağlayıcı, credential, sandbox ve exact erişim yöntemi.

### Q4 — RPO/RTO: `A`

- Başlangıç hedefi `RPO=15 dakika`, `RTO=4 saat`.
- On-prem/hibrid ana çalışma ve tesis dışı şifreli yedek.
- Production kapısı: gerçek restore tatbikatı + DB/belge checksum mutabakatı.
- Gerçek hacim ölçülünce yeniden doğrulanır.

### Q5 — Yıllık dönem + retention ayrımı

- Yıllık dönemler; yeni döneme yalnız açık kalem/işlemler kaynak bağıyla carry-forward/snapshot.
- Geçmiş kesinleşmiş olaylar kopyalanmaz ve kapalı dönemde değişmez.
- Yetkili çok-yıllı rapor/AI analizi türetilmiş read model üzerinden çalışabilir.
- Fiziksel purge yalnız en yüksek yetki + legal hold kontrolü + ikinci onay + audit.
- Otomatik purge varsayılan kapalı.
- Kalan: belge/audit/idempotency türü bazlı yasal süreler ve legal-hold rol matrisi.

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

## Bu turdan sonra gerçekten açık kalan kararlar

- `OPEN-001-GIN`: gerçek GIN unvanı ve VKN.
- `OPEN-014`: gerçek sunucu/GPU/UPS/ağ/yedek envanteri.
- `OPEN-015`: gerçek sipariş/müşteri/eşzamanlı kullanıcı/belge-OCR hacmi ve production SLO.
- `OPEN-016`: sağlayıcı/credential/sandbox ve exact entegrasyon yöntemi.
- `OPEN-018/021`: yasal saklama süreleri, legal hold ve partition/imha matrisi.
- `OPEN-019`: gerçek logo/antet/font assetleri ve exact onay permission'ı.
- `OPEN-020`: yerel model/GPU PoC; bulut sağlayıcı/KVKK/veri sınıfı/aktivasyon.

Kapanan alt bölümler yeniden `NEEDS-SINAN` olarak sorulmaz.
