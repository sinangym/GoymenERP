# Sinan Kararları — 2026-11-07 (K3'te sınırlı DELTA-OK)

Bu belge önceki karar dosyalarını silmez; GOV-D1'i ekler. Güvenlik değişmezleri (AGENTS.md "Mimari değişmezler",
"Yasaklar", K3 kapsamı, insan kapıları) değişmez.

**Kaynak:** Sinan'ın 7 Kasım 2026'da koordinatör oturumunda (`session_015Usc2qbq4qsgayZcDY3UTb`) verdiği cevap,
aynen: *"Bilmiyorum tavsiyen yönünde"*. Soru şuydu: K3 PR'larında her yeni head iki yeni tam review mi ister, yoksa
DELTA-OK yeterli mi? Bu karar koordinatörün tavsiyesidir; Sinan tavsiyeyi kabul etmiştir.

**Bağlam:** 21 Ekim kararı (satır 3) ve v4 §1'e göre K3'te delta modu yoktu; her yeni head iki yeni tam review
isterdi. Uygulamada #902, #914 ve #928'de K3 head'leri için `DELTA-OK` kullanıldı.

| No | Karar |
|---|---|
| GOV-D1 | K3 PR'ında, iki bağımsız tam review (A ve B) alınmış bir head'den sonra gelen yeni head için `DELTA-OK` **yalnız delta mekanikse** yeterlidir. Mekanik delta şunlardan ibarettir: main merge'ü ve çakışma çözümü (iki tarafın davranışı korunarak), üretici araçla yeniden üretilen dosyalar (recovery envanteri, parmak izleri, OpenAPI, relational inventory), test adı/biçim düzeltmesi, yorum/doküman. Delta; iş mantığı, migration/constraint, para/formül/tolerans, yetki/rol, modüller arası sözleşme, `.github/**` veya production yüzeyine dokunuyorsa iki yeni tam review gerekir. `DELTA-OK`'u A ve B review'larından birini yazan bağımsız reviewer verir; reviewer deltanın mekanik olduğunu açıkça yazar (`git diff <onaylı head>..<yeni head>` incelendi, dokunulan dosyalar listelendi). Şüphede iki tam review. Fresh CI şartı her head için aynen sürer. |

**Geçmiş uygulama:** #902, #914 ve #928'deki K3 `DELTA-OK` kullanımı geriye dönük açılmaz. Bu karardan sonra GOV-D1
uygulanır.
