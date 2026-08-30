# Göymen ERP — Karar Paketi (Terminal İnsan Girişi)

`docs/governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md` bölüm 7 ve 18 uyarınca.

Ajanlar otonom çalışırken karara bağlanmamış her noktayı buraya **append** eder. Sinan, Dalga 5
sonunda tek oturumda çözer. Bu dosya append-only'dir; girişler silinmez, `ÇÖZÜLDÜ:` ön ekiyle kapatılır.

## Format

```text
### ASSUMPTION-NNN  |  OPEN-XXX blocker  |  P0-BLOCKER
- Alan:
- Nokta:
- Uygulanan varsayım:
- Dayanak:            (DEC-XXX | Master §N | "dış bilinmeyen, güvenli placeholder")
- Config / feature-flag:   (ad, mevcut değer, açık/kapalı)
- Yanlışsa etki:
- Sinan'dan gereken:
- Bekleyen dilim:     (BACKLOG id | "yok — flag açılınca canlı")
- Kayıt eden:         (codex | claude | chatgpt) — <tarih> — <PR#>
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
| OPEN-010 | DBS/teminat haircut oranları + kullanılabilirlik | tür/banka/vade yapısı; oran 0 veya manuel onaylı | risk motoru canlı öncesi |
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

_(ajanlar buraya ekler)_

## P0 blocker kayıtları

_(yalnız güvenle azaltılamayan P0 güvenlik/veri-bütünlüğü bulguları)_
