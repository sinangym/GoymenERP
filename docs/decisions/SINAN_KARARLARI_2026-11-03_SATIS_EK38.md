# Sinan Kararları — 2026-11-03 (bağlı tedarik/üretimi olan satırın revizyonla kaldırılması)

Bu belge önceki karar dosyalarını silmez; SAT-D219'u ekler. PR #931'in (S-1, `ISalesOrderLinkedSupplyReadPortV1`) K3 review'unda
PR #926 (EK24 SAT-D167 satır revizyonu) ile etkileşimi olarak açılan soruyu cevaplar. SAT-D12, SAT-D167, EK36 SAT-D211 geçerlidir.

**Kaynak:** Sinan'ın 3 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevap: bir tur, bir soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevap

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Bir sipariş satırına bağlı açık bir satın alma siparişi ya da üretim emri varsa, o satır revizyonla kaldırılabilsin (veya ürünü değiştirilebilsin) mi? | Hayır, engellensin (Önerilen) | "Bağlı açık tedarik/üretim varken satır kaldırılamaz; önce satın alma/üretim tarafında kapatılır ya da bağı çözülür. Sevk edilmiş satırdaki kuralın aynısı." (seçilmeyen: "Kaldırılabilsin, uyarı versin" — "Satır kaldırılır; bağlı satın alma/üretim açık kalır ve ekranda uyarı görünür, ilgili kişi kendisi kapatır.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D219 | Bağlı açık tedarik/üretimi olan satır | Bir sipariş satırına bağlı **açık satın alma siparişi veya üretim emri** varsa o satır revizyonla **kaldırılamaz ve ürünü değiştirilemez**; önce satın alma/üretim tarafında kapatılır ya da bağı çözülür (soru 1). Kural sevk edilmiş satır kuralıyla (SAT-D167) aynıdır. Uygulama ayrıntısı (geri alınabilir): bağ `ISalesOrderLinkedSupplyReadPortV1` (S-1) ile okunur; bağ okunamazsa kaldırma reddedilir (fail-closed); bağ kaynağının hiç olmadığı (adaptör yok) durumda davranış iptal/zorla kapatma önizlemesindeki `NoSource` ile tutarlı tutulur | PR #926, PR #931 |

## Sınırlar

- Bu karar merge kapılarını ve insan kapılarını değiştirmez.
