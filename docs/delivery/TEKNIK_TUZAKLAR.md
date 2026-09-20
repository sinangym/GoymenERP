# Teknik tuzaklar — tekrarlayanlar ve doğru desen

Bu dosya, **birden fazla şeridi birden fazla kez ısırmış** teknik tuzakları ve her birinin doğru
desenini tutar. Ölçüt dar: bir tuzak buraya ancak farklı şeritlerde tekrarladığında girer; tek
seferlik bir hata kendi PR'ının yorumunda kalır.

Not: `#253` (reusable module delivery lessons) merge edilirse bu içerik oraya taşınabilir; o zamana
kadar tekrar eden tuzakların tek adresi burasıdır.

---

## 1. Koleksiyon üyeli `record`'larda eşitlik referansla karşılaştırır

**Belirti.** `Assert.Equal` iki bildirim/görünüm için "Collections differ" diyor, ama yazdırdığı
beklenen ve gerçek satırlar **birebir aynı görünüyor**. Saatler, olmayan bir farkı aramakla geçiyor.

**Sebep.** C# `record`'unun sentezlenmiş `Equals`'ı her üyeyi o üyenin kendi `Equals`'ıyla
karşılaştırır. `IReadOnlyCollection<string>` / `IReadOnlyList<T>` gibi bir üye için bu **referans**
karşılaştırmasıdır. Farklı kaynaklardan üretilmiş yapısal olarak özdeş iki değer asla eşit olmaz.
Bu bir hata değil, dilin varsayılanı; ve bir wire sözleşmesine yalnız testler için özel `Equals`
yazılmaz.

**Bu projede ısırdığı yerler (bir gecede üç kez, 19 Eylül 2026):**

| Yer | Üye |
|---|---|
| `#355` MFG-BAL kütle dengesi görünümü | `MissingParameters` |
| `#360` MFG-ORD emir listesi karşılaştırması | sıra + koleksiyon |
| `#362` ACC-V0b `acc-staged` profil testi | `ShellCapabilityDeclaration.RequiredPermissions` |

**Doğru desen.** Bütün record'u karşılaştırma. Her bildirimi **tek bir karşılaştırılabilir değere**
indirge — bütün üyeler artı koleksiyon **sırasıyla** birleştirilmiş — ve projeksiyonları koleksiyon
olarak karşılaştır:

```csharp
private static string Comparable(ShellCapabilityDeclaration declaration) => string.Join(
    '\u001f',
    declaration.CapabilityId, declaration.RouteId, declaration.RoutePath, declaration.ScreenId,
    declaration.RoutePrecedence.ToString(), declaration.ContextRequirement.ToString(),
    declaration.ScopeRequirement.ToString(),
    declaration.ImplementationAvailable ? "available" : "staged",
    declaration.GlobalProductOperation?.ToString() ?? "<no-global-operation>",
    declaration.ParentCapabilityId ?? "<no-parent>",
    string.Join(',', declaration.RequiredPermissions));

Assert.Equal(expected.Select(Comparable).ToArray(), actual.Select(Comparable).ToArray());
```

**Ne YAPMA.** İddiayı `Contains`'e veya sayıya indirme. Bu testlerin varlık sebebi genelde
"sahibinin bildirdiklerini açar ve **başka hiçbir şey** yapmaz" cümlesidir; `Contains` tam da o
cümleyi kaybeder. Projeksiyon zayıflatma değildir: her üye hâlâ iddia edilir, eklenen/düşen/değişen
bir bildirim hâlâ testi kırar.

**Testin içine sebebini yaz.** Bir sonraki okuyucu deseni değil tuzağı kopyalamasın diye,
karşılaştırmanın neden projeksiyonla yapıldığı iki satırla orada dursun.

**Öneri (ORG, #360 turunda).** Her şeridin bunu yeniden keşfetmesi yerine tek bir paylaşılan
assertion yardımcısı — ya da bu tipteki `record` karşılaştırmalarını yakalayan bir analyzer kuralı —
konulabilir. Bu bir karar değil, öneridir: paylaşılan yardımcı merkezi bir dosyadır, yeri ve sahibi
koordinatörün dağıtımına bağlıdır.

---

## 2. Suite seed çıktıları yalnız GUID veya aktör kimliği olabilir

**Belirti.** Tarayıcı koşusu **seed adımında** ölür; hiçbir ekran açılmaz.

**Sebep.** `tools/validate_browser_acceptance.py` içindeki `seed_export`, kapalı listedeki her
çıktının değerini kontrol eder: sıfır olmayan bir GUID, kanonik bir aktör ya da
`<suite>-<slug>` kalıbı. Serbest metin (bir müşteri adı, bir adres etiketi, bir para birimi kodu)
reddedilir.

**Bu projede:** `l04` (`customerName`) ve `s04` (`deliveryAddressText`, `deliveryContactName`,
`currencyCode`) — ikisi de 19 Eylül 2026'da, tarayıcı koşusundan önce yakalandı.

**Doğru desen.** Seed yalnız **kimlik** taşısın; spec, deterministik metni kendi sabiti olarak
iddia etsin. Satırı yazdıktan sonra doğrulamanın kendi kuralıyla sına — tahmin etme:

```python
import importlib.util, sys
spec = importlib.util.spec_from_file_location('b', 'tools/validate_browser_acceptance.py')
b = importlib.util.module_from_spec(spec); sys.modules['b'] = b; spec.loader.exec_module(b)
b.seed_export('l04', {...})   # kabul etmiyorsa satır yanlış, koşu değil
```

---

## 3. Bir sabiti doğrulatırken kendi sonucunu söyleme

**Sebep.** Bağımsız kontrol, kontrolcü sayıyı önce görmüşse bağımsız değildir (anchoring). Sonuç
yine doğru olabilir — ama artık kanıt değildir.

**Doğru desen.** Kontrolcüye **girdileri** (sağlayıcı bildirimleri, kaynak dosya ve head) ve
**kalibrasyon sabitlerini** (ağacın zaten servis ettiği, başkasının doğruladığı değerler) ver;
kendi ölçtüğün değeri **verme**. Kontrolcü önce kalibrasyonu yeniden üretmeli, sonra yeni değeri
hesaplamalı. Ölçümü yapan ve satırı yazan kişi kontrolcü olamaz.

**Bu projede:** ACC-V0b host sabiti — ilk kontrol anchored kaldı (değer daha önce bir mesajda
geçmişti) ve kayda böyle geçti; kapıyı, sayıyı hiç görmemiş bir şeridin kör türetmesi kapattı.
Koordinatör 19 Eylül 2026'da bunu her cross-check için kural yaptı.

---

## 4. Desen aramasından çıkan "yok" cevabı kanıt değildir

**Belirti.** Biri "şu şekilde bir şey yok" diyor, karşı taraf kabul ediyor, sonra bir şerit kodu
okuyup olanı gösteriyor. Kayda yanlış bir cümle girmiş oluyor.

**Sebep.** `grep` beklediğin şekli kodlar, aradığın şeyi değil. Eşleşme yokluğu, yalnız o desenin
yokluğudur.

**Bu projede (19 Eylül 2026).** Host'taki Cari adapter'ları sayarken `class Counterparty.*PortV1` ve
`IsCurrent()` arandı; "dört adapter var, Finance'te bu şekilde bir adapter yok" denildi ve kabul
edildi. Oysa `CounterpartyFinancePartyAccountPort` (FinanceEndpoints.cs:487) adında `V1` eki
taşımıyor ve `IsCurrent(context)` parametre alıyor — iki desen de onu asla yakalayamazdı. Doğrusu:
**beş** adapter var ve Finance aktörü ilk günden beri kontrol ediyor (`IsCurrent(FinanceContext)`,
yakalanan `ActorId` ve `CompanyId` ile).

**Dikkat: sonuç ayakta kaldı, gerekçe kalmadı.** FİNANS'ın yapacak bir işi yine yoktu — ama bu,
yöntemin doğru olduğu anlamına gelmez; şanstır. Bir iddianın doğru çıkması, onu elde etme biçimini
doğrulamaz.

**Doğru desen.**
- Negatif bir iddiayı **kümesini sayarak** kur: arayüzün tüm implementasyonları, DI'daki tüm
  kayıtlar, tablonun tüm satırları — adlandırma geleneğini grep'leyerek değil.
- Yine de grep kullanıyorsan, **hangi deseni** kullandığını ve o desenin aranan şeyi neden
  yakalaması gerektiğini birlikte söyle. Okuyan, deseni yargılayabilsin.
- "Yok" cümlesini, "şu deseni aradım, şunu buldum" cümlesinden daha güçlü kurma.
- **Kodu okuyup grep'i yalanlayan şerit daha güçlü kanıttır.** İtiraz geldiğinde kaynağı kendin aç,
  doğrula ve kaydı düzelt — ve düzeltmeyi, ilk iddiayı kabul etmiş olanlara da gönder.

---

## 5. Bir oluşturma ekranına iki giriş noktası

**Belirti.** Tarayıcı testi strict-mode ihlali veriyor: aynı erişilebilir ada sahip iki bağlantı.
Ekran okuyucu da aynı rotaya giden iki özdeş bağlantı okuyor.

**Sebep.** Ekranın hem shell nav girdisi (`CAPABILITY_NAV_LABEL`) hem de listesinin kendi birincil
bağlantısı var. LOG-4b'de tam olarak bu oldu: `/lojistik/yukleme-talimatlari/yeni` rotasına hem
sidebar'dan hem L-02 listesinden "Yeni Yükleme Talimatı" gidiyordu.

**Kural (koordinatör kararı, 19 Eylül 2026).** **Her oluşturma rotasının tam olarak BİR giriş
noktası olur:** ya sidebar girdisi (listesi ona bağlanmıyorsa) ya listenin kendi birincil bağlantısı
(bağlanıyorsa) — ikisi birden asla.

Bugünkü durum bu kurala göre: Cari, Ürün ve Sipariş taslağı yalnız sidebar'dan (listeleri bağlanmıyor,
ihlal yok, dokunulmuyor); Finance'in iki ekranı ve Muhasebe dönem önerisi yalnız listelerinden
(nav etiketi boş); Logistics tek ihlaldi ve nav etiketi kaldırılarak çözüldü — sayfa içi bağlantı
kullanıcının zaten bulunduğu bağlamda durduğu için o korundu.

**Etiket değiştirmenin dokunduğu yerler** (sürprize yer bırakmamak için): `CAPABILITY_NAV_LABEL`,
şeridin kendi `*_NAV_LABEL` tablosu, `AppShell`'in `NAV_CAPABILITIES` listesi, şeridin mount-drift
testi (değer değer karşılaştırır) ve `AppShell.test.tsx` içindeki nav iddiaları. Merkez ile şerit
aynı dilimde değişmezse drift testi kırmızıya döner.

**Otomatik pin yok, bilerek.** "Tek giriş noktası"nı mekanik olarak doğrulamak FE tarama jimnastiği
ister; tek vaka için makine kurulmadı. Kural burada, drift testi de etiketleri iki taraf arasında
zaten sabitliyor.

**Kararlaşmamış, Sinan'a ait olan kısım:** sidebar'ın oluşturma eylemi *sunup sunmaması* gerektiği.
O bir tutarlılık sorusu değil, insanların nasıl çalıştığına dair bir sorudur; persona/UX kartında
açık madde olarak durur ve bu kural onu kapatmaz.
