# kg/m başlangıç tablosu v1 (profil ve boru)

Sinan bu tabloyu 15 Kasım 2026'da başlangıç tablosu olarak onayladı (URN-D17,
`docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK7.md`). Tablo ERP'ye **seed veya migration ile yazılmaz**;
yetkili kullanıcı ürün ağırlık tablosu içe aktarma ekranından yükler (URN-D13/URN-D15/URN-D16).

- Dosya: `kgm_baslangic_tablosu_v1.csv` (720 veri satırı + başlık), SHA-256
  `6544a55322bee5ea6673d8fa5dbae604952bd86153151df3a9b39d802be85da3`.
- Kolonlar: `Group, Standard, OuterWidthMm, OuterHeightMm, OuterDiameterMm, WallMm, KgPerM, Method, SourceLabel`.
  Dikdörtgende `OuterWidthMm` büyük kenar, `OuterHeightMm` küçük kenardır; yuvarlakta yalnız `OuterDiameterMm` doludur.
- Gruplar: SHS_ERW 115, RHS_ERW 187, SHS_ERW_PAINTED 115, RHS_ERW_PAINTED 187 (geometri boyasızla aynı), CHS_ERW 85,
  PIPE_EN10255_L/M/H 9 + 11 + 11.
- Yoğunluk 7,85 kg/dm³. Formüller (EN 10219-2 kesit özellikleri):
  - yuvarlak `M = (D − T) · T · 0,02466` kg/m;
  - kare/dikdörtgen `A = 2T(B + H − 2T) − (4 − π)(Ro² − Ri²)` mm², `M = A · 0,00785` kg/m;
  - köşe yarıçapı T ≤ 6: Ro = 2T, Ri = T; 6 < T ≤ 10: Ro = 2,5T, Ri = 1,5T; T > 10: Ro = 3T, Ri = 2T.
  EN 10255 boruları aynı yuvarlak formülle, nominal dış çapla, düz uç olarak hesaplandı.
- Yayımlanmış tablolarla karşılaştırmada (19 ölçü) en büyük sapma %0,36'dır.
- İçe aktarmada her satır zorunlu kaynak etiketi taşır (URN-D14): **"Formülle hesaplandı (EN 10219-2, 7,85)"**.
  Tedarikçi belgesi veya kantar ölçümü geldikçe satır, yeni kaynak etiketiyle düzeltilir.
