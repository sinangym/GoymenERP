# kg/m başlangıç tablosu v1 (profil ve boru)

Bu tablo, URN-D17 kararının (SINAN-CEVAP 28; `docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK7.md`, karar PR'ı
#1171) veri dosyasıdır. Karar dosyası ayrı PR'da olduğundan veri bu PR'da gelir. Tablo ERP'ye **seed veya migration
ile yazılmaz**; yetkili kullanıcı ürün ağırlık tablosu içe aktarma ekranından yükler (URN-D13/URN-D15/URN-D16).

- Dosya: `kgm_baslangic_tablosu_v1.csv`. 720 veri satırı ve bir başlık satırı var. SHA-256:
  `f2192bab28201139eb848c3477f56f07d443f50cf3aa492770c3948c237bb43f`.
- Kolonlar: `Group, Standard, OuterWidthMm, OuterHeightMm, OuterDiameterMm, WallMm, KgPerM, Method, SourceLabel, Note`.
  - Dikdörtgende `OuterWidthMm` büyük kenar, `OuterHeightMm` küçük kenardır.
  - Yuvarlakta yalnız `OuterDiameterMm` doludur.
  - `SourceLabel`, içe aktarmada zorunlu kaynak etiketidir (URN-D14).
  - `Note`, hesaplama notudur ve içe aktarılmaz.
- Gruplar:

  | Grup | Satır |
  |---|---|
  | SHS_ERW | 115 |
  | RHS_ERW | 187 |
  | SHS_ERW_PAINTED | 115 |
  | RHS_ERW_PAINTED | 187 |
  | CHS_ERW | 85 |
  | PIPE_EN10255_L | 9 |
  | PIPE_EN10255_M | 11 |
  | PIPE_EN10255_H | 11 |

  Boyalı grupların geometrisi boyasız gruplarla aynıdır. Boya kütlesi hesaba katılmadı.
- Kaynak etiketleri:
  - EN 10219-2 satırları (689 satır): **"Formülle hesaplandı (EN 10219-2, 7,85)"** (URN-D17 (3)).
  - EN 10255 boruları (31 satır): **"Formülle hesaplandı (EN 10255, 7,85)"**. Bu borular EN 10219-2 kapsamında
    değildir, bu yüzden etiket kendi standardını adlandırır. Bu etiket SINAN-CEVAP 42.4 ile onaylandı
    (`SINAN_KARARLARI_2026-11-16_DALGA4_EK20.md` URN-D24, #1186).
  - L serisi (9 satır) ayrıca **"L serisi ölçüleri doğrulanacak"** notunu taşır. Bu satırların dış çap ve et
    kalınlığı henüz bir standart tabloyla doğrulanmadı. Doğrulanmış veri sayılmaz.
- Yoğunluk 7,85 kg/dm³. Formüller (EN 10219-2 kesit özellikleri):
  - yuvarlak: `M = (D − T) · T · π · 7,85 / 1000` kg/m. Tabloda katsayı yuvarlanmış olarak `0,02466` kullanıldı
    (π · 7,85 / 1000 = 0,0246615…). Tam katsayıyla fark en çok 0,0005 kg/m'dir (göreli en çok %0,03). Değerler
    3 ondalığa yuvarlıdır.
  - kare/dikdörtgen: `A = 2T(B + H − 2T) − (4 − π)(Ro² − Ri²)` mm², `M = A · 0,00785` kg/m;
  - köşe yarıçapı T ≤ 6: Ro = 2T, Ri = T; 6 < T ≤ 10: Ro = 2,5T, Ri = 1,5T; T > 10: Ro = 3T, Ri = 2T.
  - EN 10255 boruları aynı yuvarlak formülle, nominal dış çapla, düz uç olarak hesaplandı.
- Yayımlanmış tablolarla sapma: Taslak hazırlanırken 19 ölçü yayımlanmış tablolarla karşılaştırıldı ve en büyük
  sapma %0,36 bulundu. Bu karşılaştırmanın ölçü listesi ve kaynakları bu depoda yoktur. Rakam doğrulanmış kabul
  edilmez. Tedarikçi belgesi veya kantar ölçümü geldikçe satır yeni kaynak etiketiyle düzeltilir.
