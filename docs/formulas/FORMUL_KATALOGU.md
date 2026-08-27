# Formül Kataloğu

Master Rapor v3.0 içindeki formül tablolarından **83 benzersiz FormulaId** çıkarılmış ve `FORMUL_KATALOGU.csv` dosyasına aktarılmıştır.

Her formül production etkisi vermeden şu metadata’yı taşımalıdır:

- FormulaId ve sürüm
- İş anlamı, iş sahibi ve teknik sahibi
- Girdi/çıktı birimleri
- Geçerlilik tarihi
- Yuvarlama ve null/sıfır davranışı
- Tolerans ve istisna
- Teorik/fiilî ayrımı
- Kaynak kod/test referansı
- Normal, sınır, geçersiz ve yüksek hacim golden testleri
- Plan/iş emri snapshot’ı

UI, API ve rapor aynı backend calculator servisini kullanır. Formül değişikliği yeni sürümdür; geçmiş kayıt sessizce yeniden hesaplanmaz.
