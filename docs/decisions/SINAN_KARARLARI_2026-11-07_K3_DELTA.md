# Sinan Kararları — 2026-11-07 (K3'te sınırlı DELTA-OK)

Bu belge önceki karar dosyalarını silmez; GOV-D1'i ekler. Güvenlik değişmezleri (AGENTS.md "Mimari değişmezler",
"Yasaklar", K3 kapsamı, insan kapıları) değişmez.

**Kaynak:** Issue #66 makbuzu https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6705486753 — Sinan'ın 7 Kasım
2026'da koordinatör oturumunda (`session_015Usc2qbq4qsgayZcDY3UTb`) verdiği cevap, aynen: *"Bilmiyorum tavsiyen
yönünde"*. Soru: K3 PR'larında her yeni head iki yeni tam review mi ister, yoksa DELTA-OK yeterli mi? Kabul edilen
tavsiye, aynen: *"Kısa ek inceleme (DELTA-OK) kritik PR'larda yalnız değişiklik mekanikse yeterli olacak; yeni sürüm iş
mantığına, veritabanına, paraya, yetkiye ya da modüller arası bağlantıya dokunuyorsa baştan iki tam inceleme
gerekecek. Emin olunamayan durumda da iki tam inceleme."* Aşağıdaki "mekanik" tanımı bu tavsiyenin uygulama
ayrıntısıdır ve dar yorumlanır.

**Açıkça:** Bu karar v4'teki "K3'te iki bağımsız exact-head review aynen korunur" ifadesini yalnız aşağıdaki mekanik
delta için yumuşatır; başka hiçbir K3 kapısını değiştirmez.

**Bağlam:** 21 Ekim kararı (satır 3) ve v4 §1'e göre K3'te delta modu yoktu; her yeni head iki yeni tam review
isterdi. Uygulamada #902, #914 ve #928'de K3 head'leri için `DELTA-OK` kullanıldı.

| No | Karar |
|---|---|
| GOV-D1 | K3 PR'ında, iki bağımsız tam review (A ve B) alınmış bir head'den sonra gelen yeni head için `DELTA-OK` **yalnız delta mekanikse** yeterlidir. Mekanik delta yalnız şunlardır: (1) **çakışmasız** main merge'ü; (2) yalnız üretici araçla yeniden üretilen dosyalardaki (recovery envanteri, parmak izleri/pin'ler, OpenAPI, relational inventory) çakışmanın, aracı yeniden çalıştırarak çözülmesi — pin farkı yalnız main'den gelen girdiyi yansıtır; (3) test **adı**/biçim düzeltmesi — assert'ler ve test gövdesi byte-eşit kalır; (4) yorum/doküman. `src/**`, `tests/**` gövdeleri veya migration'larda elle yapılan çakışma çözümü mekanik **değildir**. Delta; iş mantığı, migration/constraint, para/formül/tolerans, yetki/rol, modüller arası sözleşme, `.github/**` veya production yüzeyine dokunuyorsa iki yeni tam review gerekir. `DELTA-OK`'u A ve B review'larından birini yazan bağımsız reviewer verir; yorumda `git diff --stat <onaylı head>..<yeni head>` çıktısını ve her dosyanın hangi mekanik sınıfa (1–4) girdiğini yazar. Şüphede iki tam review. Fresh CI şartı her head için aynen sürer. Bu tanım yetmezse #66'ya NEEDS-SINAN yazılır. |

**Geçmiş uygulama:** #902, #914 ve #928'deki K3 `DELTA-OK` kullanımı geriye dönük açılmaz. Bu karardan sonra GOV-D1
uygulanır.
