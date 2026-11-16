# kg/m başlangıç tablosu v1 (profil ve boru)

Bu tablo, URN-D17 kararının (SINAN-CEVAP 28; `docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK7.md`, karar PR'ı
#1171) veri dosyasıdır. Karar dosyası ayrı PR'da olduğundan veri bu PR'da gelir. Tablo ERP'ye **seed veya migration
ile yazılmaz**; yetkili kullanıcı ürün ağırlık tablosu içe aktarma ekranından yükler (URN-D13/URN-D15/URN-D16).

- Dosya: `kgm_baslangic_tablosu_v1.csv`. 720 veri satırı ve bir başlık satırı var. SHA-256:
  `6f231f66c098b94ae2046a26b03f26bd2872ce0a5a72cd9bd732ad14090c1e6b`.
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
  - L serisi (9 satır, `PIPE_EN10255_L`): `SourceLabel` kolonunda 42.4'te onaylanan etiket aynen kullanılır.
    Bu satırların dış çap ve et kalınlığı henüz bir standart tabloyla doğrulanmadı; bu durum `Note` kolonunda
    **"L serisi ölçüleri doğrulanacak"** notuyla işaretlidir. Doğrulanmış veri sayılmaz.
- Yoğunluk 7,85 kg/dm³. Formüller (EN 10219-2 kesit özellikleri):
  - yuvarlak: `M = (D − T) · T · π · 7,85 / 1000` kg/m. Değerler tam katsayı π · 7,85 / 1000 (= 0,0246615…) ile
    hesaplandı ve sonuç 3 ondalığa yuvarlandı; 116 yuvarlak satırın hepsi bununla birebir tutar. Yuvarlanmış
    `0,02466` katsayısı kullanılsaydı 51 satır üçüncü ondalıkta farklı çıkardı.
  - CSV'nin `Note` kolonundaki 116 yuvarlak satırın yöntem notu da tam katsayı π·7,85/1000 olarak yazılıdır
    (`(D-T)*T*π·7,85/1000 (tam katsayı)`).
  - kare/dikdörtgen: `A = 2T(B + H − 2T) − (4 − π)(Ro² − Ri²)` mm², `M = A · 0,00785` kg/m;
  - köşe yarıçapı T ≤ 6: Ro = 2T, Ri = T; 6 < T ≤ 10: Ro = 2,5T, Ri = 1,5T; T > 10: Ro = 3T, Ri = 2T.
  - EN 10255 boruları aynı yuvarlak formülle, nominal dış çapla, düz uç olarak hesaplandı.
- Yayımlanmış tablolarla sapma: Taslak hazırlanırken 19 ölçü yayımlanmış tablolarla karşılaştırıldı ve en büyük
  sapma %0,36 bulundu. Bu karşılaştırmanın ölçü listesi ve kaynakları bu depoda yoktur. Rakam doğrulanmış kabul
  edilmez. Tedarikçi belgesi veya kantar ölçümü geldikçe satır yeni kaynak etiketiyle düzeltilir.
