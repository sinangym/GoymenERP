# Sinan Kararları — 2026-10-27 (Satış: vade farkı, listesiz satır, süresi geçmiş teklif onayı)

Bu belge `SINAN_KARARLARI_2026-10-25_SATIS.md`, `SINAN_KARARLARI_2026-10-26_SATIS_EK.md`,
`SINAN_KARARLARI_2026-10-27_SATIS_EK2.md`, `SINAN_KARARLARI_2026-10-27_SATIS_EK3.md` ve
`SINAN_KARARLARI_2026-10-27_SATIS_EK4.md` dosyalarını silmez; onlara SAT-D42…SAT-D44'ü ekler.
**SAT-D44, EK4'teki SAT-D40'ın yerine geçer** (daha yeni kullanıcı kararı).

**Kaynak:** Sinan'ın 27 Ekim 2026 Claude Code cloud oturumundaki (oturum
`session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru cevapları. Sorular PR
[#835](https://github.com/sinangym/GoymenERP/pull/835) (EK4) bağımsız review'larının P2/P3 bulgularından
çıktı ([review A](https://github.com/sinangym/GoymenERP/pull/835#issuecomment-6441742353),
[review B](https://github.com/sinangym/GoymenERP/pull/835#issuecomment-6441761772)). Seçmeli sorularda
Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi. İki soruda Sinan seçenek yerine kendi
metnini yazdı; o metin aynen verilir.

| Soru | Cevap (seçilen seçenek veya Sinan'ın kendi metni) | Seçilen seçeneğin açıklaması |
|---|---|---|
| İskonto liste fiyatına göre ölçülüyor (SAT-D35). Satırda vade farkı da varsa, iskonto limit kontrolü vade farkından önceki mi sonraki mi fiyata bakılsın? | Vade farkından önce | "Liste fiyatı ile iskontolu fiyat (vade farkı eklenmeden) karşılaştırılır. Vade farkı iskontoyu gizleyemez." |
| Liste fiyatı bulunmayan (elle fiyatlanmış) satırda iskonto kontrolü nasıl olsun? | Kendi metni: "Liste fiyatı yoksa elle yazılabilir, hangi fiyat olursa olsun onaya gider" | — |
| Süresi geçmiş teklif onayında 'hazırlayan' kim sayılsın? (Teklif birkaç kez revize edilmiş olabilir.) | Kendi metni: "Oluşturanda revize edende onaylayanda görünsün, onaylama yetkisi olan her zaman onaylayabilsin" | — |

Üçüncü soruda gösterilen seçenekler şunlardı; hiçbiri seçilmedi: "Oluşturan + son revize eden"
(ikisi de onaylayamaz), "Yalnız ilk oluşturan" (ilk açan onaylayamaz), "Teklife dokunan herkes"
(hiçbiri onaylayamaz).

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D42 | İskonto ölçümünde vade farkı (SAT-D35'in uygulaması) | İskonto limit kontrolü **vade farkı eklenmeden önceki** iskontolu birim fiyatı liste fiyatıyla karşılaştırır. Vade farkı iskontoyu gizleyemez | API-12 (#834) |
| SAT-D43 | Liste fiyatı olmayan satır | Liste fiyatı referansı olmayan satıra fiyat **elle yazılabilir**; böyle her fiyatlı satır, fiyat ne olursa olsun ve iskonto alanlarından bağımsız olarak **yönetici onayına gider** (SAT-D37 onaycısı). EK4'teki "Liste fiyatı olmayan satır" açık maddesini kapatır | API-12 (#834) |
| SAT-D44 | Süresi geçmiş teklifin yönetici onayı — kişi (SAT-D24; **SAT-D40'ın yerine geçer**) | (1) Teklifi **oluşturan, revize eden ve onaylayan** kişiler teklifte görünür. (2) Süresi geçmiş teklifin siparişe dönüştürülmesini **onaylama yetkisi olan kişi her zaman onaylayabilir**; teklifi oluşturmuş veya revize etmiş olması onayı engellemez. Bu, `SINAN_KARARLARI_2026-10-21.md` P6'daki "hazırlayan ≠ onaylayan" ilkesine **süresi geçmiş teklif onayı için açık bir istisnadır** (SAT-D22'deki talep onayı istisnası gibi); başka onaylarda — SAT-D37 iskonto istisnası dahil — P6 aynen geçerlidir. Onay yetkisinin adı uygulama PR'ında permission manifest'e eklenir; kime verileceği grant kararıdır ve insan kapısında kalır | SAT-D24 takip işi (API-23), UI-20 |

## EK4 metin netleştirmeleri (anlam değişmez)

Bunlar PR #835 review'larındaki P3 notlarıdır; EK4'teki kararların anlamını değiştirmez, okunuşunu
netleştirir:

- **SAT-D34:** "limit yoktur" ifadesi "sınırsız" anlamında değildir: kullanıcının **onaysız iskonto
  hakkı yoktur** (limit %0 sayılır) ve her pozitif iskonto yönetici onayına gider.
- **SAT-D37:** "Müdür rolü" ifadesi `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md`'deki **Müdür
  personasıdır**; Kimlik'te bugün bu adla bir rol kodu yoktur ve bu karar rol kodu veya seed açmaz.
  İzin Critical değildir.
- **EK4 "Sınırlar":** EK4'teki insan kapıları listesinde `grant/rol/Critical` de değişmeden kalır;
  tek istisna SAT-D37'deki `sales.orders.discount-exception.approve` → Müdür personası eşlemesidir.

## Açık kalanlar

- Yok (bu belgedeki sorular cevaplandı). SAT-D44'teki onay yetkisinin kime verileceği grant kararıdır.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings,
  grant/rol/Critical, production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol
  veya grant açılmaz.
- SAT-D44'ün P6 istisnası yalnız süresi geçmiş teklifin siparişe dönüştürülme onayı içindir.
