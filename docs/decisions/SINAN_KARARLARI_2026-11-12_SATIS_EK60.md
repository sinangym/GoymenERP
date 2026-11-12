# Sinan Kararları — 2026-11-12 (Satış: fason hizmet faturasında kg kaynağı)

Bu belge önceki karar dosyalarını silmez; **SAT-D309**'u ekler. URT-D16 ("fatura hizmet bedelidir"), SAT-D195, SAT-D209,
SAT-D11 ve SAT-D110 aynen geçerlidir; bu belge onların metnini değiştirmez. `KARAR_SICILI` değişmez.

**Kimlik:** main `149f516ae`'de son Satış kimliği SAT-D307'dir. SAT-D308 açık #1108'de (Stok,
`SINAN_KARARLARI_2026-11-12_STOK_TRANSFER_IPTAL_VE_GELECEK_TEDARIK.md`, fire anahtarı kapalıyken fire maliyeti) ve ona
atıf yapan Üretim dalında kullanılıyor; çakışmamak için bu karar **SAT-D309** alır. Açık `claude/*` ve `codex/*` dallarında
SAT-D309 kullanan dosya yoktur (`git grep`, 2026-11-12). #1108 numarasını değiştirirse bu dosya değişmez.

**Kaynak:** Sinan, Issue #66 yorumu
[6820601088](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820601088) (11. tur, madde 4;
2026-11-12T10:03:40Z; koordinatör oturumunda `AskUserQuestion` ile alındı). Birincil yorumdan `gh api` ile doğrulandı;
koordinatör aktarımı karar kaynağı sayılmadı. Cevap kelimesi kelimesine:

> **11. tur, m.4 — F-012 fason hizmet bedeli kg kaynağı (#1115 spec NEEDS-SINAN-2):** **Teslim tartısı net kg** (önerilen
> "üretimde işlenen girdi kg" seçilmedi). Bedel, müşteriye geri teslim edilen malın tartıdaki net kg'ı üzerinden hesaplanır;
> fire bedele girmez. Fatura Satış'taki fason hizmet siparişinden kesilir.

Aynı madde Lojistik şeridinde **LOJ-D9** olarak kayda geçmiştir (`SINAN_KARARLARI_2026-11-12_LOJISTIK_EK2.md`, PR #1115).
Aşağıdaki karar metni LOJ-D9'un karar metniyle birebir aynıdır; yalnız "Etkilenen yer" Satış tarafını gösterir.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Fason hizmet bedeli hangi kg üzerinden hesaplanır? | **Teslim tartısı net kg** | Bedel, müşteriye geri teslim edilen malın tartıdaki net kg'ı üzerinden hesaplanır; fire bedele girmez. Fatura Satış'taki fason hizmet siparişinden kesilir. Seçilmeyen (önerilen): "üretimde işlenen girdi kg". Yorumda öteki seçeneklerin açıklaması yer almaz; uydurulmadı. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D309 (= LOJ-D9) | Fason hizmet bedeli kg kaynağı | Bedel kg'ı = müşteriye geri teslim edilen malın **teslim tartısı net kg**'ı (WGT-001 net). Fire bedele girmez. Fatura Satış'taki fason hizmet siparişinden kesilir; Lojistik fatura kesmez, yalnız net kg olgusunu okuma portuyla sunar. | Satış fason hizmet siparişi faturası (`docs/delivery/SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1.md` F1/F2), `SalesInvoiceCalculator` (tek hesaplayıcı), Lojistik net kg okuma portu (F1), Finans `sales.invoice.finalized.v1` tüketimi |

## Sınırlar

- Net kg okuma portunun sahibi ve anahtarı (Lojistik önerisi: talimat + tartım kimliği; "teslim tartısı = talimatın tek
  tartı kaydı", kesin tanım fason teslim dilimi spec'inde), bir tartımın birden çok fason satırına/siparişine bölüşümü,
  kısmi teslim ve tartım düzeltmesinin faturaya etkisi bu kararla **seçilmedi** (plan N-7/N-8; gerekirse `NEEDS-SINAN`).
- Birim fiyat, net kg'ın faturaya ondalık hassasiyeti, satır tutarı yuvarlaması ve sipariş kg'ı ile fark toleransı
  (`OPEN-004` ilişkili) bu kararla seçilmedi (plan N-9). Tevkifat kodu SAT-D110'a göre Ayarlar'dadır.
- Kod/şema/migration yoktur; yeni tablo/ekran açılmaz (SAT-D209 sınırı).

İnsan kapıları değişmez: deployment, environment, secret, Settings'ten workflow etkinleştirme, grant/rol/Critical,
production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` somut kullanıcı kararı ister. Bu belge hiçbirini
vermez.
