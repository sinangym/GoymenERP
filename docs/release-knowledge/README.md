# Yayındaki sürümün davranış bilgisi (WP-A4, DALGA_PLANI_v2 A.4 (3))

Bu klasör, yapay zekâ bilgi tabanının **üçüncü kaynağının** girdisidir: yayımlanmış sürüme bağlı kullanım notları.
Her not bir dosyadır (`RK-*.md`). Üretici `tools/generate_ai_release_knowledge.py` notları okur ve
`contracts/ai/release-knowledge-v1.json` paketini yazar; paket API'nin yayın çıktısına girer.

Kurallar:

- Bir not pakete yalnız `durum: yayinda` ise **ve** her `kanit:` satırındaki dosya bu ağaçta varsa ve verilen ifadeyi
  içeriyorsa girer. Henüz uygulanmamış özellik (`durum: uygulanmadi`) ya da kanıtı bulunamayan not pakete metniyle
  girmez; yalnız kimliği ve dışlanma nedeni listelenir. Böylece araç uygulanmamış bir özelliği anlatamaz.
- `yetki:` satırı notun gösterilebilmesi için kullanıcının sahip olması gereken izinleri sayar (`-` = ek izin yok);
  her izin `ErpPermissions` içinde kayıtlı olmalıdır. Araç notu yalnız bu izinlerin hepsine sahip kullanıcıya döndürür.
- Kişi adları ve iletişim/kimlik bilgileri üretimde maskelenir (plan D2, AI-D36); bulut uygunluğu içerik ve veri
  sınıfından türetilir (AI-D34 d).
- Paket, araçta kullanılmadan önce `ai.knowledge.approve` iznine sahip bir kişinin onayını ister (AI-D32 "onaylı
  kaynak"); izin hiçbir role verilmez.

Biçim:

```
---
kimlik: RK-<ALAN>-<KONU>
baslik: <Türkçe başlık>
durum: yayinda | uygulanmadi
yetki: - | <izin>[, <izin>...]
kanit: <depo yolu> | <dosyada geçen ifade>
---
<Türkçe kullanım notu; yalnız bu sürümde var olan davranış>
```
