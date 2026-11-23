# Kişisel Asistan Hafızası — Çalışan Bilgilendirme Metni (TASLAK v1)

> **Hukuk onayı bekleyen TASLAK; onaylanınca sürüm numarası Ayarlar'a girilir ve hafıza açılır.**
>
> - Dayanak: AI-D50 (E-87). İlgili kararlar: AI-D15, AI-D30, AI-D38, AI-D39, AI-D42, AI-D44, AI-D48, AI-D49, YOL-D43.
> - Bu metin hukukçu onaylayıp metin sürümü Ayarlar'a girilene kadar yayımlanmaz; o zamana kadar kişisel asistan hafızası
>   **kapalıdır** (AI-D50). Aylık yapay zekâ bütçesi belirlenene kadar bütün yapay zekâ özellikleri ayrıca kapalıdır (YOL-D43).
> - Mevzuat maddeleri, veri sorumlusu unvanı, hukuki sebep ve başvuru yolu bilerek yazılmamıştır; hukukçu `[...]` yer
>   tutucularını doldurur. Bu taslak yeni karar, izin ataması veya saklama/imha politikası değildir.
> - Köşeli parantezli sayılar (200 kayıt, 1 yıl) bugünkü kararlı değerlerdir; Ayarlar'dan değişirse metnin yeni sürümü
>   hazırlanır.

---

## Asistan hafızası nedir?

Göymen ERP'de her kullanıcının kendi asistanı vardır. Asistan, sizin yetkinizle ve sizin verdiğiniz bilgilerle çalışır.
"Hafıza", asistanın sonraki konuşmalarda işinize daha uygun cevap verebilmesi için **sizin kaydettiğiniz kısa notlardır**.
Örnek: "Raporları ay sonu kapanışına göre göster", "Bana önce geciken tedarikçi teslimlerini söyle".

Hafıza sizin yetkinizi genişletmez. Asistan, hafızada yazsa bile, sizin göremediğiniz bir kaydı size göstermez.

## Ne saklanır?

- **Sizin yazdığınız hafıza kayıtları:** Kaydın metni, ne zaman eklendiği, en son ne zaman kullanıldığı ve etkin mi kapalı mı
  olduğu.
- **Asistanın bu kayıtları okuması:** Asistan size cevap hazırlarken etkin hafıza kayıtlarınızı okur. Hangi kaydın
  kullanıldığı, saklama süresini hesaplamak için "son kullanım tarihi" olarak tutulur.
- **Zamanlı görevleriniz:** Kurduğunuz düzenli görevler (örneğin her sabah onay özeti) ve durumları.

Saklanmayanlar: Hafızaya sizin eklemediğiniz bir bilgi asistan tarafından kendiliğinden hafıza kaydı olarak yazılmaz.
`[Hukukçu: ses, konum vb. başka veri toplanmadığına dair cümle gerekiyorsa buraya]`

## Ne amaçla?

- Asistanın tercihlerinizi her seferinde yeniden sormadan uygulaması.
- Zamanlı görevlerin sizin belirlediğiniz biçimde çalışması.
- Şirketin, asistanın kurallara uygun kullanıldığını denetleyebilmesi (aşağıda "Kim görür?").

`[Hukukçu: işleme amacı ve hukuki sebep ifadesi]`

## Ne kadar süre saklanır?

- Bir hafıza kaydı **son kullanıldığı tarihten [1 yıl]** sonra otomatik olarak **pasife** alınır; pasif kayıt asistan
  tarafından artık kullanılmaz.
- Pasife alınan kaydın kalıcı olarak silinmesi (fiziksel imha) bu metnin konusu değildir; şirketin ayrı saklama ve imha
  politikasına göre yapılır. `[Hukukçu: imha politikasının adı/bağlantısı]`

## Kaç kayıt tutulabilir?

- Kişi başı en fazla **[200]** etkin hafıza kaydı tutulur.
- Sınıra geldiğinizde yeni kayıt eklemek için önce eski bir kaydınızı kapatmanız gerekir.
- Sınır şirket tarafından Ayarlar'dan değiştirilebilir; değişirse bu metnin yeni sürümü yayımlanır.

## Kim görür?

- **Siz:** Bütün hafıza kayıtlarınızı ve zamanlı görevlerinizi görürsünüz, düzeltebilir ve kapatabilirsiniz.
- **Yetkili yönetici:** Bunun için ayrıca tanımlanmış "asistan hafızası gözetimi" izni verilmiş yönetici, hafıza
  kayıtlarınızı ve zamanlı görevlerinizi **görüntüleyebilir ve kapatabilir**. Bu izin başka hiçbir izinle birlikte gelmez;
  kime verileceğine şirket yönetimi ayrıca karar verir.
  - Yöneticinin her görüntülemesi ve her kapatması **denetim kaydına yazılır**.
  - Yöneticinin görüntülemesi veya kapatması size **ayrıca bildirilmez**.
  - Yönetici, sizin sildiğiniz bir kaydın metnini göremez ve kayıtlarınızı sizin adınıza değiştiremez.
- **Başka kullanıcılar:** Göremez.

`[Hukukçu: denetim kayıtlarına kimin erişebildiği ve bu kayıtların saklama süresi cümlesi]`

## Bilgileriniz nereye gider? (veri sınıfı)

- Asistan, cevabı hazırlarken bilgiyi şirketin belirlediği yapay zekâ modellerine gönderir. Bilginin hangi modele
  gidebileceği **veri sınıfına** göre belirlenir.
- Kişisel veri (örneğin TC kimlik numarası, telefon, IBAN) içeren yazılar **"Gizli"** sınıfına yükselir. **Gizli içerik
  şirket dışındaki (bulut) modellere gönderilmez**; yalnız şirket içi modelde işlenir.
- **Para tutarı veya kişi adı** (maskelenmiş olsa bile) içeren karar ve notlar da yalnız şirket içi modelde işlenir.
- Bu nedenle hafızanıza kişisel veri yazmamanızı öneririz; yazarsanız bu kurallar uygulanır.

`[Hukukçu: yurt dışına aktarım ve kullanılan hizmet sağlayıcılar hakkında gerekli cümle]`

## Haklarınız

- Hafıza kayıtlarınızı ve zamanlı görevlerinizi **görme**.
- Yanlış veya güncelliğini yitirmiş kaydı **düzeltme**.
- Bir kaydı veya bütün hafızanızı **kapatma / silme**. Sildiğiniz kayıt asistan tarafından kullanılmaz ve yönetici de
  metnini göremez.
- `[Hukukçu: mevzuattan doğan diğer haklar ve başvuru usulü]`

## İletişim

Sorularınız ve talepleriniz için: `[Hukukçu/şirket: veri sorumlusu unvanı, birim, e-posta veya başvuru kanalı]`

---

## Hazırlayan notu (yayımlanmaz)

| Metindeki ifade | Kaynak karar |
|---|---|
| Kişisel asistan, kullanıcının yetkisiyle çalışır | AI-D15 |
| Yönetici görür ve kapatır; ayrı izin; görüntüleme denetime yazılır | AI-D30; izin adı `work.assistant-memory.oversee` (rol önerisi ROL-D24: SYS; atama insan kapısı) |
| Görüntüleme/kapatma kullanıcıya bildirilmez | AI-D39 |
| Son kullanımdan 1 yıl sonra pasif; fiziksel imha ayrı politika | AI-D38 |
| Kişi başı 200 etkin kayıt; Ayarlar'dan değişir | AI-D48 |
| Kişisel veri içeren metin Gizli; Gizli buluta gitmez | AI-D44 (sınıf yükselmesi); Gizli = yalnız şirket içi model (AI-D42, AI-D45) |
| Para tutarı veya kişi adı içeren içerik yalnız şirket içi modelde | AI-D49 |
| Hafıza, hukuk onayı ve metin sürümü girilene kadar kapalı | AI-D50 |
| Bütçe belirlenene kadar bütün yapay zekâ özellikleri kapalı | YOL-D43 |

- "Yönetici sildiğiniz kaydın metnini göremez" ifadesi `work.assistant-memory.oversee` izninin kayıttaki tanımından
  alınmıştır (tren-2 `ErpPermissions.cs` açıklaması); hukukçu ve teknik sahip birlikte teyit eder.
- Açık noktalar (hukukçuya): denetim kaydının saklama süresi; zamanlı görevlerin saklama süresi (AI-D38 yalnız hafıza
  kaydını kapsar); metnin hangi kanaldan ve hangi anda (ilk kullanımda onay ekranı mı, duyuru mu) gösterileceği.
