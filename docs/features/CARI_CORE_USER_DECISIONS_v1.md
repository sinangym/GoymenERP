# Cari Core — Kullanıcı Kararları v1.0

- Tarih: 2026-08-28
- Kaynak: 28 Ağustos 2026 kullanıcı kabulü
- Kapsam: `CARI-GATE-01…04` iş kararlarını kapatır
- Yetki: Bu belge, aynı PR içindeki daha eski readiness/domain önerilerindeki `CARI-GATE-01…04` açık ifadelerini bu dört konu için supersede eder.
- Sınır: `task_manifest.json`, global Karar Sicili, ADR register veya platform görevleri bu belgede değiştirilmez.

## CARI-DEC-001 — Tek Party + şirket bazlı CariHesap + yetkili grup görünümü

**Durum: KABUL / CARI-GATE-01 CLOSED**

1. Aynı hukuki karşı taraf Göymen grubunda **tek `Party`** kaydıyla temsil edilir.
2. Party, bir veya birden fazla Göymen hukuki şirketiyle çalışabilir.
3. Her Göymen şirketi için ayrı **`CompanyPartyAccount` / `CariHesap`** kaydı oluşur.
4. `CompanyPartyAccount` zorunlu `CompanyId` taşır; şirket bazlı cari ilişki, rol ve finansal/operasyonel bağlar birbirine karıştırılmaz.
5. Kullanıcı yalnız yetkili olduğu Göymen şirketlerine bağlı Party/CariHesap verisini görebilir. Yetkisiz şirkete ait ilişki; liste, detay, count, arama, hata veya toplam üzerinden sızamaz.
6. Kullanıcı birden fazla Göymen şirketine yetkiliyse aynı Party için **birleşik grup görünümü** açabilir.
7. Birleşik grup görünümü yalnız kullanıcının yetkili olduğu `CompanyPartyAccount` kayıtlarını kapsar.
8. Birleşik görünümde Party tek kart gibi gösterilebilir; şirket kırılımı her zaman açılabilir/drill-down yapılabilir.
9. Bakiye ve hareket kaynakları fiziksel olarak şirket bazında ayrı kalır. Birleşik görünüm yeni ledger/hareket üretmez ve CompanyId sınırlarını kaldırmaz.
10. Kullanıcı isterse yetkili şirketlerin bakiyesini, karşı taraf tek şirketle çalışıyormuş gibi **read-only birleşik yürüyen bakiye görünümü** olarak görebilir.
11. Bu birleşik bakiye yalnız sunum/analizdir; şirketler arası otomatik mahsup, ödeme dağıtımı, kapama, muhasebe kaydı veya hukuki netleştirme oluşturmaz.
12. Aynı para birimindeki yetkili şirket bakiyeleri birleşik görünümde signed toplam/yürüyen bakiye olarak gösterilebilir; her toplamın şirket kırılımı korunur.
13. Farklı para birimleri otomatik olarak tek tutara çevrilmez. Para birimi bazında ayrı toplam gösterilir; ileride onaylı kur/raporlama motoru varsa ayrıca açıkça seçilen çevrilmiş rapor görünümü sunulabilir.
14. Yetkisiz bir şirketin bakiyesi birleşik toplamın içine dahi katılamaz.

### Kabul örneği

Party `P1`, Göymen şirket A ve B ile çalışıyorsa:

- `Party=P1` tektir.
- `CompanyPartyAccount(A,P1)` ve `CompanyPartyAccount(B,P1)` ayrıdır.
- Yalnız A yetkili kullanıcı sadece A ilişkisini ve A bakiyesini görür.
- A+B yetkili kullanıcı A ve B'yi ayrı kırılımda görebilir ve isteğe bağlı grup görünümünde aynı para birimi bakiyelerini read-only toplam olarak görebilir.
- Bu toplam A ve B arasında muhasebesel mahsup değildir.

## CARI-DEC-002 — PartyKind kapsamı

**Durum: KABUL / CARI-GATE-02 CLOSED**

İlk sürüm iki ana taraf türünü birlikte destekler:

- `LegalEntity` — tüzel kişi/firma.
- `NaturalPerson` — gerçek kişi; şahıs işletmesi de hukuki kişi yaratılmadan bu tür altında ticari ad/unvan bilgileriyle temsil edilebilir.

Kurallar:

1. Party teknik kimliği UUID'dir ve türden bağımsızdır.
2. Tür bazlı zorunlu/opsiyonel resmi kimlik alanları ayrı validasyon profiliyle uygulanır.
3. Sistem VKN/TCKN/MERSİS gibi alanların her Party türünde zorunlu olduğunu varsaymaz; ülke/hukuki tür kuralı ayrıca deterministik validation policy'de tanımlanır.
4. Bir şahıs işletmesi için aynı gerçek kişi yalnız ticari rol değişti diye ikinci Party olarak çoğaltılmaz; mükerrer kontrol uygulanır.

## CARI-DEC-003 — Grup genelinde tek ve değişmez CariCode

**Durum: KABUL / CARI-GATE-03 CLOSED**

1. Her `Party` için grup genelinde tek görünen Cari Kodu vardır.
2. Cari Kodu şirket bazında yeniden üretilmez.
3. İlk format: `CAR-000001`, `CAR-000002`, … şeklinde grup genelinde benzersiz sıra.
4. Cari Kodu oluşturulduktan sonra değişmez/reuse edilmez.
5. Teknik primary identity yine immutable UUID'dir; CariCode kullanıcıya görünen arama/iş anahtarıdır.
6. Şirket bazlı dış muhasebe/entegrasyon kodu gerekiyorsa `CompanyPartyAccount` üzerinde ayrı integration/external-code alanında tutulur; CariCode yerine geçmez.
7. Mükerrer merge ileride kabul edildiğinde eski teknik kimlik ve eski CariCode geçmiş/alias olarak korunur; kod sessizce başka Party'ye devredilmez.

## CARI-DEC-004 — Kritik master değişikliği için tek yetkili onayı

**Durum: KABUL / CARI-GATE-04 CLOSED**

### Onaya gidecek kritik değişiklikler

Aşağıdaki değişiklikler doğrudan ana kayda uygulanmaz; önce değişiklik talebi/preview oluşturulur:

- resmi unvan / legal name,
- VKN/TCKN veya diğer resmi kimlik numarası,
- MERSİS / resmi sicil kimliği,
- IBAN / banka hesap kimliği ekleme, değiştirme veya pasife alma.

Akış:

`mevcut değer → önerilen değer → fark/etki önizleme → gerekçe → onaya gönder → tek yetkili onayı → command → audit`

Kurallar:

1. **İki ayrı yetkili onayı gerekmez. Bir yetkili onayı yeterlidir.**
2. Değişikliği/talebi oluşturan kullanıcı kendi kritik değişikliğini onaylamaz; maker-checker ayrımı korunur.
3. Onaylayıcı kullanıcının ilgili CompanyId ve approval permission kapsamı server-side doğrulanır.
4. Onay sırasında kayıt sürümü/öneri hash'i tekrar doğrulanır; onay beklerken kaynak değişmişse stale proposal uygulanmaz.
5. AI öneri/taslak/fark hazırlayabilir fakat onay veremez.
6. Adres, telefon, e-posta ve normal iletişim/muhatap değişiklikleri uygun update yetkisi + audit ile doğrudan uygulanabilir; ayrı approval gerekmez.
7. Cariyi pasife alma ilk sürümde ayrı approval gerektirmez; uygun yetki + gerekçe + expected version + idempotency + audit zorunludur.
8. Cariyi yeniden aktifleştirme ayrı yetkili command + audit ile yapılır; bu karar kapsamında ikinci/onay kuyruğu zorunlu değildir.

## CARI-GATE-05 — C-12 temel takip görevi

Bu kullanıcı kararları `CARI-GATE-05` konusunu kapatmaz. C-12 İlişki/Takip Görevleri ilk `CARI-CORE-1` kodlamasını bloke etmez ve ayrı kullanıcı kabulüyle ele alınacaktır.

## Teknik uygulama sonucu

Bu dört iş kararıyla aşağıdaki production-domain tasarımı artık iş kararı açısından açılmıştır:

- `Party` + `CompanyPartyAccount` modeli,
- `PartyKind = LegalEntity | NaturalPerson`,
- grup genelinde immutable `CariCode`,
- yetkili şirket birleşik Party/group read modeli,
- kritik master değişiklik proposal + tek approver maker-checker akışı.

Ancak implementation yine mevcut platform güvenlik kapılarına tabidir: CompanyId, RBAC/ABAC, audit, idempotency/concurrency, hassas identifier storage/index/log-redaction ve gerçek SQL acceptance doğrulanmadan merge/canlı kabul yapılmaz.
