# TREN-1 — W7 Finans

FIN-SC36 / FNS-D11, FNS-D22, FNS-D87, FNS-D102: iç defter bakiye kaynağı ve aynı işlem
koruması yazıldı. Dar kaynak incelemesi uygulama kabulü veya bağımsız K3 imzası değildir.
Tam birim/gerçek SQL/HTTP/Vitest/tarayıcı/CI koşumları **NOT_RUN**.

## Bakiye kaynağı

- Banka/kasa onaylı açılış hareketleri ve tersleri; bankada tahsilat/tersi ve tahsil edilen
  müşteri evrakı; ödeme hareketi/tersi; kaydedilmiş masraf; tek virman kaydının ilgili bacağı.
  Taslak, onay bekleyen kayıt, dağıtım, kredi planı veya gelecek vade para hareketi sayılmaz.
- POS bankaya net geçen tutarı kullanır; müşterinin brüt tutarı ikinci bir banka girişi
  sayılmaz. Virman iki ayrı para hareketine kopyalanmaz.
- Tam ondalık metin BigInteger tabanlı mevcut Finans türüyle toplanır. Döviz dönüşümü,
  scale veya yuvarlama seçilmez. İşlem/valör tarihine göre eski bakiye kesiti alınmaz;
  kontrol FNS-D22 uyarınca kayıt anındaki mevcut kaydedilmiş defter üzerindedir.
- Şirket/hesap/para birimi uyuşmazlığı, bozuk tutar veya bilinmeyen immutable para olayı
  sıfır bakiyeye çevrilmez. Açılış var ama masraf/virman yok diye kasa sıfır sayılmaz.

## İşlem koruması

`InternalLedgerTreasuryBalanceGuard` gerçek ilişkisel Finance context'inde mevcut
SERIALIZABLE transaction ister. Aynı context ile bakiye okunur, tek para olayı/komut/audit
kaydedilir ve transaction commit edilir. Korumasız bir bakiye okuması yeterli sayılmaz.

Kapsam: banka ödemesi kesin kaydı, masraf kesin kaydı, virman preview/commit,
tahsilat ters kayıt onayı, açılış onayı/ters kayıt onayı ve kasa pasiflik öneri/onayı.
Tahsilat ve açılış tersleri de bankadan çıkıştır; harcanmış açılışın ters kaydı hesabı
eksiye götüremez. Cari açılış tersinde yeni bir bakiye yasağı seçilmez.

Callback örtük tekrar çalışmaz. SQL deadlock/kalıcılık hatası geçici Unavailable olarak
döner, transaction rollback edilir, tracker ve pending audit temizlenir; aynı anahtar
yeniden denenebilir. Var olan daha zayıf izolasyonlu caller transaction'ı yükseltilmez.
Serializable caller transaction'ında commit sahibi caller'dır; hata tüm katılımı geri alır.
Legacy Save yardımcıları concurrency/unique hatasını sonuca çevirip kısmen çalışmış SQL
komutlarını commit edemez: korumalı deneme işareti provider transaction'ı sonlandırsa da
kalır, hata dış transaction sahibine yeniden fırlatılır ve bütün işlem geri alınır.
Bu koruma MARS açıkken EF savepoint bulunmaması ihtimalini de kapsar; ortam ayarı değişmez.
Şirket/aktör/yetki her mevcut çitte, hatadan sonra ve save öncesinde yeniden ölçülür.
Yeni raw SQL, bypass, migration, THROW, izin, grant veya rol ataması yoktur.

Yeni System.Data/Common namespace kullanımı yalnız mevcut Finance transaction'ının
izolasyonunu okumak ve onu yönetmek içindir; merkezi guard gerekçesi K3 review bekler.
Legacy Finance audit mapping kapsamı genişletilmedi; yeni Audit owner portu yalnız pending
taslakları iptal edilen denemeden temizlemek için üretim factory'lerine bağlıdır.

## Test kaynakları ve arayüz

- Banka ledger golden: açılış/ters, POS net/ters, müşteri evrakı, ödeme/ters, kaydedilmiş
  masraf ve virman bacakları; taslaklar hariç. Küçük kesir, bozuk veri, şirket/kur uyumu
  ve kasa açılışının sıfır sayılmaması InMemory kaynaklarıyla sınanır; SQL kabulü değildir.
- Gerçek SQL fact kaynakları:
  `FinanceExpensePaymentSqlServerRelationalAcceptanceTests.Internal_ledger_serializes_two_outgoing_records_and_same_key_retry_never_overdraws_the_approved_opening`;
  `FinanceExpensePaymentSqlServerRelationalAcceptanceTests.Reversing_a_spent_bank_opening_is_refused_without_changing_the_opening_or_consuming_the_approval_key`.
  Ek iki kaynak: `Partial_SQL_save_failure_rolls_back_the_ledger_audit_and_key_before_retry`
  ve `Partial_SQL_save_failure_rolls_back_the_ledger_audit_and_key_even_with_MARS`.
  İkisi de gerçek canonical debit UPDATE çalıştıktan sonra sentetik EF hatası üretir;
  kayıt/anahtar/audit rollback'ini ve aynı anahtarın sonraki başarı/replay'ini arar.
  Gerçek bakiye/transaction kullanılır; yetki, hassasiyet profili ve dönem portları açıkça
  sentetiktir. İlk fact iki gerçek korumalı okumayı eşzamanlar; en çok bir çıkış, kaybedenin
  anahtarının tüketilmemesi, tekrarın yetersiz bakiye reddi ve kazanan replay'i aranır.
- Mevcut gerçek SQL reversal fixture'ları gerçek guard'a bağlıdır. Ayrık InMemory policy
  fixture'ları adı açık sentetik guard kullanır; bunlar üretim bakiye kanıtı değildir.
- Giden banka ödemesinde yetersiz/okunamayan bakiye Türkçe açıklanır; onaylı kayıt kesin
  kaydedilmiş gösterilmez. Açılış/tahsilat ters kayıt mesajları da kaynak dosyasındadır.
  İki DOM refusal test kaynağı eklendi; frontend fiyat/bakiye hesabı yapmaz.

## Kalan Finans işleri

FIN-S13 alış fatura olayı/yevmiye üreticisi, FIN-S14 banka/kasa yevmiye kaynakları,
FIN-S17 dönemler arası devir, FNS-SC32 cari bakiye devir fişi ve FIN-SC47 kredi ekranı/
fiş önerisi/kapalı dönem düzeltmesi bu değişiklikle bitmez. Kredi alan belgesi taslaktır;
onay, rol/grant, production, OPEN durumları ve Finance STOP değiştirilmedi.

## Derleme kaydı

Finance.Infrastructure ve Finance.UnitTests kaynak derlemeleri exit 0, sıfır hata/uyarı.
Frontend `tsc -b` exit 0. Api.IntegrationTests derlemesi exit 0, sıfır hata/uyarı.
Test koşumları yapılmadı; eski Finans kırmızılarının geçtiği iddia edilmez.
