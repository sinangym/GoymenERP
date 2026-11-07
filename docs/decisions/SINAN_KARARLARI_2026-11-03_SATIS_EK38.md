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
| SAT-D219 | Bağlı açık tedarik/üretimi olan satır | Bir sipariş satırına bağlı **açık satın alma siparişi veya üretim emri** varsa o satır revizyonla **kaldırılamaz ve ürünü değiştirilemez**; önce satın alma/üretim tarafında kapatılır ya da bağı çözülür (soru 1). Kural sevk edilmiş satır kuralıyla (SAT-D167) aynıdır. SAT-D167'nin kaldırma/ürün değişikliği engellerine (sevk, yükleme talimatı, stok tahsisi) **açık bağlı satın alma/üretim** de eklenir. Uygulama ayrıntısı (geri alınabilir): bağ `ISalesOrderLinkedSupplyReadPortV1` (S-1) ile okunur; "açık" olup olmadığı sahip modülün cevabıdır (`Present`), Satış yorumlamaz. Bağ okunamazsa kaldırma ve ürün değişikliği reddedilir (fail-closed). Bağ kaynağı yalnız hiçbir modül satış siparişi satırına satın alma/üretim bağı **kaydetmiyorsa** `NoSource` sayılır ve engellemez (engellenecek bağ yoktur); bir modül bu bağı kaydetmeye başladığında adaptörü aynı teslimde bağlanır, bağı kaydeden ama adaptörü kayıtlı olmayan tür `Unreadable` sayılır ve kaldırma ve ürün değişikliği reddedilir. Bağ, revizyon gönderilirken ve onaylanıp uygulandığı son adımda yeniden okunur; o anda açık bağ varsa işlem 409 ile reddedilir. Kaldırılması onay bekleyen satıra yeni bağ kurulmasının engellenip engellenmeyeceği bu kararla seçilmez (`NEEDS-SINAN`) | PR #926, PR #931 |

## Sınırlar

- Bu karar merge kapılarını ve insan kapılarını değiştirmez.
