# Küçük kullanıcı akışı için çalışma biçimi

Durum: mevcut kuralları uygulamaya yardımcı öneri. Yeni CI job'ı, zorunlu artefakt şeması, zaman SLA'sı veya test muafiyeti getirmez.

## 1. Başlamadan kısa ortak not

Koordinatör mevcut görev/PR açıklamasına aşağıdaki bilgileri koyar; ayrı dosya ve karşılıklı alındı turları gerekmez. Geçerli kaynak mevcutsa yeniden analiz edilmez.

```text
AKIŞ / BİTİŞ: Kullanıcı eylemi -> gözlenebilir sonuç; kapsam dışı etkiler.
KAYNAK: Güncel karar/spec/acceptance maddeleri; main ve ilgili API kaynağı.
SAHİPLER: Mevcut Business / Backend / Frontend görevleri; shared dosya sahibi.
ÖRNEK: Yeniden kullanılacak kaynaklar; yeni modülün farkı.
SÖZLEŞME: Yetki kapsamı; request/response; version; hata/replay; yan etki.
ERKEN KONTROL: Bu akışta en pahalı yanlış varsayımı ne yakalayacak?
KANIT: Hedef test + gerekli SQL/browser/CI/review; uygulanamazsa gerekçe.
BEKLEYEN: Tek somut bağımlılık -> sahibi -> çıkış şartı; bağımsız hazır iş.
```

Business birkaç normal/negatif kabul örneği verir. Eksik işletme/para/stok/üretim/yetki kararı tahminle kapatılmaz; yalnız etkilediği iş bekler. Geri alınabilir teknik seçim mevcut yetki ve hakemlikte çözülür. Sahiplik belirsizken aynı uygulamanın ikincisi açılmaz.

## 2. Kod büyümeden en riskli varsayımı ölç

- Mevcut runtime pinleri, lockfile, worktree, branch ve yazılabilir yollar kontrol edilir. Bu bir yeni tam derleme zorunluluğu değildir.
- Gerçek API hazırsa ilk basit okumada doğru origin, aktör, yetki/capability ve DTO doğrulanır. Yazma probuyla uyumluluk denenmez.
- API hazır değilse iki taraf kabul edilmiş aynı sözleşmeyle bağımsız hazırlanır; ilk uygulanabilir anda gerçek API ile buluşur. Kontrollü fixture entegrasyon PASS'i sayılmaz.
- Yeni/değişen test kaynağı typecheck ve uygun test discovery ile yüklenebilirlik açısından kontrol edilir. `--list` config import'unun sıfır yan etkili olduğu varsayılmaz; mevcut runner'ın gereksinimleri okunur. ENV_READY veya test acceptance sonucu uydurulmaz.
- Paylaşılan dialog/focus/company switch ve gerçekten pending retry gibi riskli sıraya kısa hedef kontrol uygulanır. Sadece sayfa render olması yeterli değildir.

## 3. Önce küçük gerçek akış, sonra ilişkili istisnalar

Backend endpoint -> frontend consumer -> ilk kullanılabilir yol erken birleşir. Yetki/şirket sınırı, idempotency, version ve audit ilk uygulamadan itibaren korunur; "sonra ekleriz" denmez. Yeni formun her katmanını ayrı uzun seri handoff'a dönüştürme.

| Değişiklik | Geliştirme sırasında erken doğrulama | Teslimde |
|---|---|---|
| Belge/link/metadata | Kaynak, link ve kapsam farkı | İlgili mevcut belge kapıları; sırf belge için ürün E2E'si yok |
| Decoder/form/retry/UI katmanı | Typecheck + gerçekten ilgili helper/DOM testi; gerektiğinde controlled browser | Etkilenen gerçek kullanıcı akışı ve zorunlu CI/review |
| Endpoint/transaction/yetki | Hedef contract, authorization/version/replay testi | Gereken gerçek SQL ve failure/browser kanıtı; risk sınıfı kapıları |
| Migration/formül/modüller arası sözleşme | Somut golden/constraint/rollback veya forward-fix incelemesi | K3 gereksinimleri; iki bağımsız review; ilgili gerçek kabul |
| Test ortamı/source-binding | İlgili producer-consumer fixture/negatif test | Test edilen kaynak/binary/çalışan ortam eşleşmesi ve gerekli CI |

Bu tablo zorunlu CI kapsamını daraltmaz. Kapanmış başarılı test/review yalnız kaynak değişimi, yeni hata veya somut çözülmemiş risk gerektiriyorsa yenilenir. Review yeni commit'e kendiliğinden taşınmaz; aynı reviewer gereken farkı ve son kaynak durumunu değerlendirir.

## 4. Başarısızlığı doğru sınıflandır

1. Ürün mü, test beklentisi mi, ortam/araç mı, yoksa eksik iş kararı mı? Ham hata/trace ile sınıflandır; kanıt yoksa neden bilinmiyor yaz.
2. Aynı ilgili sahip en küçük teknik düzeltmeyi yapar. Başarısızlığı skip, zorla tıklama, timeout'u sürekli büyütme veya beklentiyi anlamsızlaştırmayla gizleme.
3. Hedef kontrolle düzeltmeyi erken doğrula; final kaynak değiştiyse mevcut binding/CI/review kurallarını uygula. Eski canlı ortamı yeni kaynakmış gibi yeniden etiketleme.
4. Aynı engelle davranışsız ikinci turda yeni status mesajı yerine nedeni, sahibi ve çözüm eylemini netleştir; mevcut governance dur/escalation kuralına uy. Yeni politika veya mimariyi sessizce seçme.

## 5. Teslim ve bekleme yönetimi

- CI/ortam/inceleme bekleyişinin tek sahibi ve tek izleyicisi olur. Diğerleri mevcut sonucu tüketir; ayrı watcher açmaz.
- Hazır API şeması veya kaynak devri consumer hazırlığını başlatabilir; gerçek browser koşusu yetkili ENV_READY'yi bekler. Böylece iki taraf birbirinin finalini beklemez.
- Her değişiklik için yeni run zorunluluğu burada icat edilmez; mevcut doğrulayıcı ne gerektiriyorsa uygulanır. Kaynak doğrulamasını gevşetme önerisi ayrı inceleme konusudur.
- Canlı kaynak/test/DB/browser kimlikleri mevcut tek ortam sahibi tarafından bağlanır. SQL post-state, tarayıcı, cleanup farklı iddialardır; ölçülmeyen aralık NOT_MEASURED kalır.
- Merkezde kısa durum yeterli: çalışan davranış, tek kalan engel/sahibi, sıradaki eylem. Ham log/hash aynı mevcut artefaktlarda; birden çok yeni rapor kopyası yok.
- Dilim bitince coordinator geçerli yetkiyle merge eder; uygulama yazarı self-merge etmez. Tüm modül, mobil, gerçek IdP veya production kabulü dar akıştan çıkarılmaz.

## 6. Öğrenmenin gerçekten hız kazandırmasını ölç

Bir sonraki yetkili birkaç akışta mevcut zaman damgalarından şu kısa özeti çıkar: ilk gerçek çalışan akışa kadar süre; kod düzeltme / test-ortam düzeltme / CI-inceleme / insan-araç bekleme süreleri; ilk gerçek koşum sonucu; aynı sınıf hatanın tekrarı. Süreler örtüşüyorsa toplanmaz; ölçülemeyen bölüm bilinmiyor kalır. Yeni telemetri sistemi kurma.

Hedef commit/test sayısını artırmak değil, doğru kullanıcı akışını daha az tekrar ile teslim etmektir. Evrensel "3 kat hız" veya bütün ERP için kanıtsız ay tahmini verilmez. Ortak kod gerçekten eksikse ihtiyacın çıktığı dar implementation işinde geliştirilir; sırf öğrenme paketi için bütün modüller yeniden yazılmaz.
