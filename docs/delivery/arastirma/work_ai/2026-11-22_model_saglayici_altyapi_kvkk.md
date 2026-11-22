# Model, sağlayıcı, altyapı ve KVKK: ERP içi yapay zekâ asistanı için seçenekler (22 Kasım 2026)

> Ajan araştırma çıktısıdır; **karar değildir**. Repoda hiçbir dosya değiştirilmedi, commit/push yapılmadı; yalnız bu not dosyası yazıldı. Amaç iki açık karara destek: (1) yerel model + GPU donanımı (OPEN-014 / OPEN-020 yerel PoC), (2) bulut sağlayıcının açılıp açılmayacağı ve nasıl açılacağı (OPEN-020, AI-D5/D6). Önceki notun (`docs/delivery/arastirma/2026-11-22_kanitlanmamis_ai_yontemleri.md`) konuları (ajan kalıpları, hafıza mimarisi, değerlendirme CI'ı, CaMeL, Whisper Türkçe WER aralığı, WhatsApp politikası vb.) burada tekrar edilmedi.

**Okuma notu — kanıtın niteliği (rapor yazarı mutlaka okusun):**

- **Araç kısıtı.** Bu oturumda web araması yalnız ilk dört sorguda çalıştı (oturumun ortak arama bütçesi doldu). Sayfa açma yalnız oturum ağında izinli alan adlarında çalıştı. **Açılabilenler:** `platform.claude.com`, `www.anthropic.com`, `raw.githubusercontent.com`, `pypi.org`. **Denenip açılamayanlar** (oturum ağ ayarında izin gerekir): `kvkk.gov.tr`, `developers.openai.com`, `ai.google.dev`, `arxiv.org`, `huggingface.co`, `privacy.claude.com`, `www.unite.ai`, `onyx.app`, `github.com`. Ağ izin listesi dar olduğu için **denenmeyenler:** `cloud.google.com`, `learn.microsoft.com`, `aws.amazon.com`, mevzuat ve donanım satıcı siteleri.
- **Bulgu etiketleri.** **[BİRİNCİL]** = bu oturumda okunan birincil kaynak (resmî doküman, proje README'si, lisans metni). Anthropic'in iki hukuk sayfası (Ticari Şartlar, Veri İşleme Eki) ve desteklenen ülkeler sayfası araç tarafından soru-cevap biçiminde alıntılanarak okundu; tam metin görülmedi. **[KATALOG]** = topluluk bakımındaki fiyat/katalog dosyası (ikincil; 22.11.2026'da indirildi; resmî fiyat sayfası açılamadı). **[SNIPPET]** = yalnız arama özeti, sayfa okunmadı. **[DOĞRULANMADI]** = yalnız modelin eğitim bilgisine (yaklaşık 2026 ortası) dayanır; bu oturumda kaynak açılamadı. [DOĞRULANMADI] maddeler "Bulgular"a değil "Çıkarımlar" veya "Boşluklar"a yazıldı.
- **Kanıt düzeyi.** A = hakemli yayın / bağımsız denetim; B = bağımsız benchmark veya analist; C = satıcı/proje beyanı (**[İDDİA]**); D = spekülatif / varsayım; **M = mevzuat (hukukçu teyidi gerekir)**.
- **Tarih.** Aksi yazılmadıkça fiyat ve sürüm bilgisi 22.11.2026'da okunan dosyadaki değerdir. Model ve fiyatlar hızlı değişir; karar öncesi resmî sayfadan teyit edilmelidir.
- **KVKK bölümü özellikle zayıf doğrulanmıştır:** Kurum sayfaları açılamadı. O bölümün tamamı M düzeyindedir ve hukukçu teyidi olmadan karara dayanak yapılmamalıdır.

---

## 1. Kasım 2026 itibarıyla şirket içinde çalıştırılabilen açık ağırlıklı modeller; belge/OCR, gömme (embedding), yeniden sıralama ve Türkçe konuşma tanıma

### Takeaway

Tek/çift GPU'da çalışabilen güncel adaylar şunlardır: **Gemma 4 (31B, 26B-A4B, 12B)** ve **gpt-oss-20b/120b** (ikisi de birincil kaynakta Apache 2.0), **Qwen3.6/3.8-27B** (Apache 2.0 bilgisi ikincil kaynaktan) ve **Mistral Small** (lisansı teyit edilmeli); hepsi araç çağırma ve şemaya bağlı çıktı ile sunulabiliyor, fakat bu modellerin **yayımlanmış güncel Türkçe skoru bulunamadı** ve Türkçe'ye özel ince ayarlı modeller yayımlanmış ölçümlerde genel çok dilli modelleri geçmiyor. Belge okuma, gömme ve konuşma tanıma için küçük uzman modeller yerelde çalışacak olgunlukta; seçim şirketin kendi Türkçe belge ve soru setiyle yapılmalı.

### Cited Findings

**1.1 Model aileleri (lisans, boyut, bağlam)**

| Aile | Sürüm ve tarih | Boyutlar | Bağlam | Lisans | Kanıt | Kaynak |
|---|---|---|---|---|---|---|
| **Qwen (Alibaba)** | Qwen3.5: 16.02–02.03.2026; Qwen3.6: 16–22.04.2026; Qwen3.8: 12–14.08.2026 | 3.5: 397B-A17B, 122B-A10B, 35B-A3B, 27B, 9B, 4B, 2B, 0.8B · 3.6: 35B-A3B, 27B · 3.8: 2.4T-A95B, 27B | Sunum örneklerinde 262.144 | README: "license file released with the model weights". İkincil kaynakta Qwen3.8-27B ve Qwen3.6-27B için **Apache 2.0** | C [BİRİNCİL] + lisans [SNIPPET] | [Qwen3.8 README](https://raw.githubusercontent.com/QwenLM/Qwen3.8/main/README.md); [Unite.AI derlemesi](https://www.unite.ai/?p=349834) |
| **Gemma 4 (Google)** | 2026 (HF lansman yazısı) | E2B (2,3B etkin), E4B (4,5B etkin), 12B Unified, 26B-A4B (MoE, 4B etkin), 31B | E2B/E4B: 128k · 12B/26B/31B: 256K | **Apache 2** ("truly open with Apache 2 licenses") | C [BİRİNCİL] | [HF "Welcome Gemma 4"](https://raw.githubusercontent.com/huggingface/blog/main/gemma4.md) |
| **gpt-oss (OpenAI)** | 2025 | 120b: 117B (5,1B etkin) · 20b: 21B (3,6B etkin) | 128K (katalogda 131.072) | **Apache 2.0** + kısa kullanım politikası | C [BİRİNCİL] | [gpt-oss README](https://raw.githubusercontent.com/openai/gpt-oss/main/README.md); [HF yazısı](https://raw.githubusercontent.com/huggingface/blog/main/welcome-openai-gpt-oss.md) |
| **Mistral** | Small 4.0 (model kartı kimliği `mistral-small-4-0-26-03`, yani Mart 2026); Large 4 | Small 3.1/3.2: 24B (açık ağırlık). Small 4 ve Large 4 boyutu görülemedi | Small 4: 262.144 · Large 4: 1.048.576 (API) | **Modele göre değişir**: Mistral Large 2 ağırlıkları MRL (ticari olmayan), Codestral MNPL; Small 4 / Large 4 açık ağırlık lisansı bu oturumda doğrulanamadı | C [BİRİNCİL] + [KATALOG] | [mistral-inference README](https://raw.githubusercontent.com/mistralai/mistral-inference/main/README.md); [LiteLLM fiyat haritası](https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json) |
| **Llama (Meta)** | Son açık nesil: Llama 4, 05.04.2025 (depoda daha yenisi yok) | Scout 17B-16E, Maverick 17B-128E | 10M / 1M | **Llama 4 Community License** (özel): dağıtımda "Built with Llama" gösterme, 700 milyon aylık kullanıcı üstünde ayrı lisans, kabul edilebilir kullanım politikası | C [BİRİNCİL] | [llama-models README](https://raw.githubusercontent.com/meta-llama/llama-models/main/README.md); [Llama 4 LICENSE](https://raw.githubusercontent.com/meta-llama/llama-models/main/models/llama4/LICENSE) |
| **Phi (Microsoft)** | Phi-4 ailesi (phi-4, phi-4-mini, phi-4-multimodal, Phi-4-reasoning, Phi-4-Reasoning-Vision-15B); Phi-5 görülmedi | README'de boyut yazmıyor (en büyüğünün adında 15B) | — | MIT (BFCL tablosundaki lisans sütunu) | C/B [BİRİNCİL] | [PhiCookBook README](https://raw.githubusercontent.com/microsoft/PhiCookBook/main/README.md); [BFCL verisi](https://raw.githubusercontent.com/ShishirPatil/gorilla/gh-pages/data_overall.csv) |
| **Kimi K3 (Moonshot)** | Tarih README'de görülmedi | 2,8T parametre | 1M | **Kimi K3 License** (özel) | C [BİRİNCİL] | [Kimi-K3 README](https://raw.githubusercontent.com/MoonshotAI/Kimi-K3/main/README.md) |
| **GLM-5.x (Z.ai)** | GLM-5, 5.1, 5.2, 5.3, 5.3-Flash (2026) | Amiral modeller çok büyük | 5.2: 1M | README'de lisans satırı görülmedi; önceki nesil GLM-4.6 BFCL tablosunda MIT | C [BİRİNCİL] | [GLM-5 README](https://raw.githubusercontent.com/zai-org/GLM-5/main/README.md) |
| **DeepSeek V4 / V4.1, MiniMax M3, Nemotron 3 / 3.5, Tencent Hy3** | 2026 | Örn. Nemotron 3: Nano 30B-A3B, Super 120B-A12B, Ultra 550B-A55B | 262k–1M | DeepSeek V4-Pro MIT, MiniMax "Community License", Kimi özel lisans (ikincil kaynak) | [KATALOG] + [SNIPPET] | [LiteLLM fiyat haritası](https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json); [tech-insider derlemesi](https://tech-insider.org/best-open-source-llm-2026/) |

- Qwen3.5 serisi için üretici beyanı: görüntü-dil birleşik model, "201 languages and dialects" desteği [İDDİA, C]. — [Qwen3.8 README](https://raw.githubusercontent.com/QwenLM/Qwen3.8/main/README.md)
- gpt-oss: "MXFP4 quantization … making `gpt-oss-120b` run on a single 80GB GPU … and the `gpt-oss-20b` model run within 16GB of memory"; modeller yalnız "harmony" biçimiyle çalışır; "native capabilities for function calling … and Structured Outputs" [C]. — [gpt-oss README](https://raw.githubusercontent.com/openai/gpt-oss/main/README.md)
- Gemma 4 üretici tablosu [İDDİA, C]: 31B için MMLU Pro %85,2, **Tau2 (araç kullanan ajan görevi, 3 koşu ortalaması) %76,9**, MMMLU (çok dilli MMLU) %88,4; 26B-A4B için Tau2 %68,2, MMMLU %86,3; 12B için Tau2 %69,0; karşılaştırma: Gemma 3 27B Tau2 %16,2, MMMLU %70,7. — [HF "Welcome Gemma 4"](https://raw.githubusercontent.com/huggingface/blog/main/gemma4.md)
- Llama 4 için Meta notu: "the Llama4 series of models require at least 4 GPUs to run inference at full (bf16) precision." — [llama-models README](https://raw.githubusercontent.com/meta-llama/llama-models/main/README.md)
- Kasım 2026 derlemesi, kendi sunucusunda çalıştırma için şu adları sayıyor: Qwen3.8, DeepSeek V4.1, GLM-5.3, Kimi K3.1 Pro, gpt-oss, Gemma 4, Mistral Small 4 ve Large 4, NVIDIA Nemotron 3.5, MiniMax M3; seçim ölçütü olarak lisans, donanım ayak izi ve çalışma zamanı desteğini öne çıkarıyor [SNIPPET, B/C]. — [Unite.AI](https://www.unite.ai/?p=349834)

**1.2 Araç çağırma güvenilirliği ve yapılandırılmış çıktı**

Berkeley Function Calling Leaderboard (BFCL V4) verisi; tablodaki en yeni model GPT-5.2-2025-12-11 olduğundan **yaklaşık Aralık 2025 görüntüsüdür**, 2026 modelleri (Qwen3.5+, Gemma 4, gpt-oss) tabloda yoktur [B]:

| Model (mod) | Genel | Tek adım (Non-Live AST) | **Çok adımlı (Multi Turn)** | Lisans |
|---|---|---|---|---|
| Claude Opus 4.5 (FC) | %77,47 | %88,58 | **%68,38** | Kapalı |
| Claude Sonnet 4.5 (FC) | %73,24 | %88,65 | %61,37 | Kapalı |
| GLM-4.6 (FC, thinking) | %72,38 | %87,56 | %68,00 | MIT (çok büyük model) |
| GPT-5.2 (FC) | %55,87 | %81,85 | %28,12 | Kapalı |
| Qwen3-32B (FC) | %48,71 | %88,77 | %47,87 | Apache-2.0 |
| Qwen3-8B (FC) | %42,57 | %87,58 | %41,75 | Apache-2.0 |
| Qwen3-30B-A3B-Instruct-2507 (FC) | %41,39 | %85,77 | %30,00 | Apache-2.0 |
| Qwen3-14B (FC) | %41,03 | %84,94 | %34,75 | Apache-2.0 |
| Llama-4-Maverick (FC) | %37,29 | %88,65 | %20,25 | Llama 4 Community |
| Mistral-small-2506 (FC) | %37,15 | %73,60 | %11,50 | — |
| Llama-3.3-70B (FC) | %31,90 | %88,02 | %21,50 | Llama 3 Community |
| Gemma-3-27b-it (Prompt) | %29,47 | %87,17 | %10,75 | Gemma kullanım şartları |
| Phi-4 (Prompt) | %28,79 | %69,56 | %3,88 | MIT |

Kaynak: [BFCL `data_overall.csv`](https://raw.githubusercontent.com/ShishirPatil/gorilla/gh-pages/data_overall.csv)

- vLLM: adlandırılmış veya `required` araç seçiminde çağrı, yapılandırılmış çıktı arka ucuyla kısıtlanır: "You are guaranteed a validly-parsable function call - not a high-quality one." `tool_choice="auto"` için sunucu tarafında `--tool-strict-level function|parameter` ile her istekte zarf veya parametre şeması zorlanabilir [BİRİNCİL]. — [vLLM tool_calling.md](https://raw.githubusercontent.com/vllm-project/vllm/main/docs/features/tool_calling.md)
- vLLM yapılandırılmış çıktı arka uçları: xgrammar veya guidance; JSON şeması, regex, seçim listesi, dilbilgisi; `lm-format-enforcer` kullanımdan kalkıyor [BİRİNCİL]. — [vLLM structured_outputs.md](https://raw.githubusercontent.com/vllm-project/vllm/main/docs/features/structured_outputs.md)
- vLLM ayrıştırıcı listesinde `openai` (gpt-oss-20b/120b), `hermes` (Qwen2.5), `mistral`, `llama3_json`, `pythonic` (Llama 4), `glm45/glm47`, `kimi_k2`, `deepseek_v3/v31`, `functiongemma` vb. var; Qwen README'si Qwen3.8 için `--tool-call-parser qwen3_coder` kullanıyor [BİRİNCİL]. — [vLLM tool_calling.md](https://raw.githubusercontent.com/vllm-project/vllm/main/docs/features/tool_calling.md); [Qwen3.8 README](https://raw.githubusercontent.com/QwenLM/Qwen3.8/main/README.md)
- llama.cpp: "Function calling is supported for all models"; yerel biçimler (Llama 3.x, Hermes/Qwen 2.5, Mistral Nemo, GPT-OSS Harmony…) dışında "Generic" işleyiciye düşer ve bu "may consume more tokens and be less efficient"; paralel araç çağrısı varsayılan kapalı. JSON şemasından dilbilgisi (`--json-schema`, `json_schema`) desteklenir [BİRİNCİL]. — [llama.cpp function-calling.md](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/docs/function-calling.md); [llama-server README](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/tools/server/README.md)
- Ollama: OpenAI uyumlu `/v1/chat/completions` uç noktasında `tools` ve `response_format` destekli; `format` alanıyla JSON şeması zorlanır ("Ollama's cloud currently does not support structured outputs") [BİRİNCİL]. — [Ollama OpenAI uyumluluğu](https://raw.githubusercontent.com/ollama/ollama/main/docs/api/openai-compatibility.mdx); [Ollama structured outputs](https://raw.githubusercontent.com/ollama/ollama/main/docs/capabilities/structured-outputs.mdx)

**1.3 Türkçe kalite — yayımlanmış skorlar**

- **TurkishMMLU** (EMNLP Findings 2024; 5 örnekli, 9 lise dersi) [A; modeller 2024 nesli]: GPT-4o 83,1 · Claude-3 Opus 79,1 · GPT-4-turbo 75,7 · Llama-3 70B-IT 67,3 · Claude-3 Sonnet 67,3 · Claude-3 Haiku 65,4 · Command-R+ 60,6 · Aya-23 35B 55,6 · Mixtral 8x22B 54,8 · Llama-3 8B-IT 46,4 · Gemma 7B 43,6 · **Trendyol-LLM 7B-C-D 34,1** · Mistral 7B-IT 32,0. — [TurkishMMLU deposu](https://raw.githubusercontent.com/ArdaYueksel/TurkishMMLU/main/README.md)
- **TR-MMLU** (2025; 280.000 soruluk havuzdan 6.200 soru, 62 bölüm): GPT-4o %84,84, Claude 3.5 Sonnet %84,40 [SNIPPET, A/B]. — [arXiv 2501.00593](https://arxiv.org/html/2501.00593v2)
- **Cetvel** (EACL 2026; 23 görev, 7 kategori, 70B'ye kadar 33 açık ağırlıklı model): Türkçe'ye özel talimat-eğitimli modeller, Llama 3 ve Mistral gibi çok dilli/genel modellere göre **genel olarak daha düşük** performans gösteriyor [SNIPPET, A]. Görev listesi (soru-cevap, çoktan seçmeli, NLI, sınıflandırma, çeviri, özet, dilbilgisi düzeltme) [BİRİNCİL]. — [EACL 2026 ön baskı](https://preview.aclanthology.org/ingest-eacl/2026.eacl-long.46.pdf); [Cetvel README](https://raw.githubusercontent.com/KUIS-AI/cetvel/main/README.md)
- **MMLU-Pro-TR** küçük tablosu (2024) [C/B]: Meta-Llama-3-8B-Instruct 20,93 · Llama-3-8B üzerine Türkçe ince ayarlar 20,81–27,00 · **Trendyol-LLM-7b-chat-v1.8 18,15 · Turkcell-LLM-7b-v1 17,15** (taban Llama-3-8B'nin altında). — [MMLU-pro-TR README](https://raw.githubusercontent.com/bezir/MMLU-pro-TR/main/README.md)
- Türkçe ince ayar örneği [C]: Qwen2.5-14B-Instruct taban MMLU_TR 59,47 / XCOPA_TR 66,80 / XNLI_TR 41,53; ince ayarlı Turkish-LLM-14B 59,77 / 66,00 / 43,33 (fark +0,30 / −0,80 / +1,80). — [Turkish-LLM README](https://raw.githubusercontent.com/ogulcanaydogan/Turkish-LLM/main/README.md)
- Karşı örnek (Eylül 2026 ön baskısı; 7–8B sınıfı, Türkçe alan belgeleri, getirme destekli): Trendyol-8B %75, Qwen2.5-7B %65 [SNIPPET, C]. — [arXiv 2609.28007](https://arxiv.org/html/2609.28007v1)
- **TurkBench** (Ocak 2026; 8.151 örnek, 21 alt görev; çevrimiçi lider tablosu var) — skorlar özetlerde görünmedi [SNIPPET]. — [arXiv 2601.07020](https://arxiv.org/html/2601.07020v2)
- Anthropic'in resmî çok dillilik tablosunda (Global-MMLU, İngilizce skora oran) **Türkçe yok**; örnek satır Arapça: Fable 5.2 %99,6 · Opus 5.5 %99,3 · Sonnet 5.5 %98,3 · Haiku 5.5 %96,6. Not: "Use the table to compare languages within a model, not to rank models"; "Test with any languages relevant to your specific use cases" [C]. — [Anthropic çok dillilik](https://platform.claude.com/docs/en/build-with-claude/multilingual-support)

**1.4 Belge anlama ve OCR**

- **OmniDocBench v1.6** uçtan uca tablosu (benchmark sahibi MinerU ekibi; belgeler Çince/İngilizce) [B−]: OXR-1.0 (1,3B) 97,03 · TeleOCR 96,91 · OvisOCR2 96,47 · MiniCPM-OCR 1.0 96,44 · **PaddleOCR-VL-1.6 (0,9B) 96,34** · MinerU2.5-Pro 95,75 · GLM-OCR 95,22 · PaddleOCR-VL 94,18 · **Gemini 3 Pro 92,91** · Gemini 3 Flash 92,62 · dots.ocr 90,77 · DeepSeek-OCR 2 90,25 · Qwen3-VL-235B 89,78 · **GPT-5.2 86,59** · MinerU-Pipeline 86,47 · olmOCR 85,74 · Mistral OCR 85,66 · Marker 78,44. — [OmniDocBench README](https://raw.githubusercontent.com/opendatalab/OmniDocBench/main/README.md)
- **olmOCR-bench** (Datalab'ın derlediği tablo) [C]: Chandra 2 85,8 · dots.ocr 1.5 83,9 · olmOCR 2 82,4 · DeepSeek OCR 75,4 · Mistral OCR API 72,0 · GPT-4o 69,9 · Qwen3-VL 8B 64,6. — [Chandra README](https://raw.githubusercontent.com/datalab-to/chandra/master/README.md)
- **Türkçe satırları** (satıcının kendi çok dilli ölçümü) [İDDİA, C]: Datalab API %88,9 · Chandra 2 %84,1 · Gemini 2.5 Flash %84,1 · GPT-5 Mini %78,2 · Chandra 1 %68,1; Surya 2.5 Türkçe %82,1 (Surya 2: %80,7). — [Chandra README](https://raw.githubusercontent.com/datalab-to/chandra/master/README.md); [Surya çok dilli tablo](https://raw.githubusercontent.com/datalab-to/surya/master/static/docs/multilingual.md)
- **Lisanslar** [BİRİNCİL]: PaddleOCR Apache 2.0 (PP-OCRv5 109 dil, dil tablosunda Türkçe `tr` var; PaddleOCR-VL 0,9B, 109 dil); docTR Apache 2.0; Docling MIT; olmOCR Apache 2.0 (en az 12 GB GPU; "less than $200 USD per million pages" [İDDİA]); **Surya ve Chandra ağırlıkları değiştirilmiş OpenRAIL-M: yalnız 5 milyon USD (Surya) / 2 milyon USD (Chandra) fon-gelir altındaki kuruluşlara ücretsiz**; MinerU "Apache 2.0 tabanlı, ek koşullu" özel lisans. — [PaddleOCR README](https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/main/README.md); [PP-OCRv5 dil listesi](https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/main/docs/version3.x/algorithm/PP-OCRv5/PP-OCRv5_multi_languages.en.md); [docTR README](https://raw.githubusercontent.com/mindee/doctr/main/README.md); [Docling README](https://raw.githubusercontent.com/docling-project/docling/main/README.md); [olmOCR README](https://raw.githubusercontent.com/allenai/olmocr/main/README.md); [Surya README](https://raw.githubusercontent.com/datalab-to/surya/master/README.md); [MinerU README](https://raw.githubusercontent.com/opendatalab/MinerU/master/README.md)
- Hugging Face karşılaştırma yazısı [B/C]: OlmOCR-2 "English-only"; PaddleOCR-VL 109 dil; DeepSeek-OCR yaklaşık 100 dil; Chandra 40+ dil. "performance across different document types, languages, etc., varies a lot … collect a dataset of representative examples". Maliyet: OlmOCR-2 için milyon sayfa başına 178 USD (H100, 2,69 USD/saat varsayımıyla) [İDDİA]. — [HF "Supercharge your OCR Pipelines"](https://raw.githubusercontent.com/huggingface/blog/main/ocr-open-models.md)
- llama.cpp çok kipli sunucu PaddleOCR-VL, GLM-OCR, DeepSeek-OCR, Dots.OCR ve HunyuanOCR modellerini çalıştırabiliyor [BİRİNCİL]. — [llama.cpp multimodal.md](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/docs/multimodal.md)
- Bulut alternatifi fiyatı: Mistral OCR sayfa başına 0,004 USD (toplu 0,002) [KATALOG]. — [LiteLLM fiyat haritası](https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json)

**1.5 Gömme ve yeniden sıralama**

- MTEB (Multilingual) ortalama görev skoru, Qwen ekibinin 6 Haziran 2025'te lider tablosundan aldığı değerler [C]: Qwen3-Embedding-8B **70,58** · 4B 69,45 · gemini-embedding-exp 68,37 · **Qwen3-Embedding-0.6B 64,33** · multilingual-e5-large-instruct 63,22 · Cohere multilingual v3 61,12 · **BGE-M3 59,56** · OpenAI text-embedding-3-large 58,93. — [Qwen3-Embedding README](https://raw.githubusercontent.com/QwenLM/Qwen3-Embedding/main/README.md)
- Yeniden sıralayıcılar (MMTEB-R) [C]: Qwen3-Reranker-8B 72,94 · 4B 72,74 · 0.6B 66,36 · Jina multilingual v2 base 63,73 · gte-multilingual-reranker-base 59,44 · BGE-reranker-v2-m3 58,36. — [Qwen3-Embedding README](https://raw.githubusercontent.com/QwenLM/Qwen3-Embedding/main/README.md)
- Qwen3-Embedding: 0.6B/4B/8B, 32K dizi uzunluğu, boyut kısaltma (MRL), talimat duyarlı; desteklenen diller listesinde Türk dilleri ailesi altında Türkçe var [BİRİNCİL]. — [Qwen3-Embedding README](https://raw.githubusercontent.com/QwenLM/Qwen3-Embedding/main/README.md)
- BGE-M3: 100+ dil, 8192 token, yoğun + seyrek + çok vektörlü getirme tek modelde; FlagEmbedding deposu MIT [BİRİNCİL]. — [BGE-M3 README](https://raw.githubusercontent.com/FlagOpen/FlagEmbedding/master/research/BGE_M3/README.md); [FlagEmbedding README](https://raw.githubusercontent.com/FlagOpen/FlagEmbedding/master/README.md)
- EmbeddingGemma: 308M parametre, 2K bağlam, 100+ dil, 768 boyut (512/256/128'e kısaltılabilir) [C]. — [HF EmbeddingGemma yazısı](https://raw.githubusercontent.com/huggingface/blog/main/embeddinggemma.md)
- Sunum: Hugging Face Text Embeddings Inference; Qwen3-Embedding, multilingual-e5-large-instruct, EmbeddingGemma ve yeniden sıralayıcıları (bge-reranker, gte-multilingual-reranker) destekler, CPU imajı vardır [BİRİNCİL]. — [TEI README](https://raw.githubusercontent.com/huggingface/text-embeddings-inference/main/README.md)
- Bulut gömme fiyatları (1M token): OpenAI text-embedding-3-small 0,02 USD, 3-large 0,13 USD; gemini-embedding-2 0,20 USD; Cohere embed-v4.0 0,12 USD [KATALOG]. — [LiteLLM fiyat haritası](https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json)

**1.6 Türkçe konuşma tanıma**

- Whisper Large v3 "remains a strong multilingual baseline, supporting 99 languages"; çok dillilik tek dil performansından ödün verir; çok dilli parkurda yalnız beş dil ölçülüyor (21.11.2025 itibarıyla) [B]. openai-whisper ve faster-whisper MIT lisanslı (faster-whisper 1.2.1, 31.10.2025). — [HF Open ASR Leaderboard yazısı](https://raw.githubusercontent.com/huggingface/blog/main/open-asr-leaderboard.md); [PyPI faster-whisper](https://pypi.org/pypi/faster-whisper/json)
- Qwen3-ASR (29.01.2026; 0.6B ve 1.7B): Türkçe desteklenen 30 dil arasında. Türkçe'nin de bulunduğu 20 dilli Fleurs alt kümesinde ortalama WER: Whisper-large-v3 6,85 · Qwen3-ASR-1.7B 6,62 · Qwen3-ASR-0.6B 10,37 (yalnız Türkçe değeri verilmemiş) [İDDİA, C]. `qwen-asr` paketi Apache-2.0. — [Qwen3-ASR README](https://raw.githubusercontent.com/QwenLM/Qwen3-ASR/main/README.md); [PyPI qwen-asr](https://pypi.org/pypi/qwen-asr/json)
- NVIDIA: Parakeet V3 / Canary V2 (Ağustos 2025) 25 Avrupa dili; Nemotron-3.5-ASR-Streaming-0.6B (Haziran 2026) 40 dil; Nemotron 3.5 Transcribe 30B-A4B (Ekim 2026) 40 dil, bir saate kadar kayıt [C]. Türkçe kapsamı bu oturumda doğrulanamadı. — [NeMo Speech README](https://raw.githubusercontent.com/NVIDIA/NeMo/main/README.md)
- Bulut fiyatları: gpt-4o-transcribe 0,0001 USD/sn (0,006 USD/dk), gpt-4o-mini-transcribe 0,00005 USD/sn; Mistral Voxtral mini 0,00005 USD/sn [KATALOG]. — [LiteLLM fiyat haritası](https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json)

### Inferences

- **Yerel aday kısa listesi (ölçülecek, seçilmiş değil):** (i) Qwen3.8-27B veya Qwen3.6-27B, (ii) Gemma 4 31B ve 26B-A4B, (iii) gpt-oss-20b (hızlı, küçük) ve donanım yeterse gpt-oss-120b, (iv) Mistral Small (lisansı teyit edilirse). Kimi K3, GLM-5.x, DeepSeek V4, Qwen3.8-2.4T gibi amiral modeller yüzlerce milyar–trilyon parametreyle KOBİ donanımının dışında kalır; yalnız barındırılan API olarak anlamlıdır.
- **Araç çağırmada asıl fark çok adımlı işte çıkıyor.** BFCL'de tek adımda küçük açık modeller de %85–90 bandında; çok adımda 2025 nesli orta boy açık modeller %10–48, en iyi kapalı modeller %61–68. Şemaya zorlanan çözümleme (vLLM strict / llama.cpp dilbilgisi) biçim hatasını ortadan kaldırır ama **doğru aracı doğru sırayla seçmeyi garanti etmez**. Gemma 4'ün Tau2 beyanı (%76,9) bu açığın 2026'da daraldığına işaret ediyor, fakat satıcı ölçümüdür ve Türkçe değildir (D).
- **Türkçe'ye özel modeller için kanıt zayıf.** Cetvel (A), TurkishMMLU (A) ve MMLU-Pro-TR aynı yönü gösteriyor: 7–8B Türkçe ince ayarlar genel çok dilli modelleri geçmiyor; tek karşı örnek 7–8B sınıfında ve hakemsiz. Bu yüzden başlangıç için "Türkçe model" aramak yerine güncel çok dilli modelleri kendi setimizle ölçmek daha savunulabilir.
- **Belge okumada küçük uzman modeller güçlü, ama ölçümler Türkçe değil.** OmniDocBench'te 0,9–1,3B'lik uzman modeller Gemini 3 Pro ve GPT-5.2'nin üstünde; satıcı ölçümünde Türkçe'de açık model (Chandra 2) ile Gemini 2.5 Flash eşit (%84,1). Lisans açısından **PaddleOCR (Apache 2.0) en temiz yol**; Surya/Chandra gelir eşiği nedeniyle büyük olasılıkla ücretli lisans ister.
- **Gömme için 0,6B sınıfı yeter.** Qwen3-Embedding-0.6B veya BGE-M3 CPU'da bile çalışır; karar ve README belgeleri küçük bir külliyat olduğundan gömme maliyeti ihmal edilebilir. BGE-M3'ün seyrek (anahtar kelime benzeri) çıktısı karar numarası, ürün kodu gibi tam eşleşme gerektiren Türkçe aramalarda işe yarayabilir (D).
- **Bellek hesabı** (aşağıdaki oranlar bağımsız ölçüm tablosundan: 70B için Q4_K_M 39,59 GB, F16 131,42 GB; yani milyar parametre başına ≈0,57 GB ve ≈1,88 GB): 27B → Q4 ≈15,3 GB, 8 bit ≈25,3 GB; 31B → Q4 ≈17,5 GB, 8 bit ≈29,1 GB; 35B → Q4 ≈19,8 GB; 120B → Q4 ≈67,9 GB. Üstüne bağlam (KV) belleği ve eşzamanlılık payı eklenir. Kaynak oran: [GPU-Benchmarks-on-LLM-Inference](https://raw.githubusercontent.com/XiongjieDai/GPU-Benchmarks-on-LLM-Inference/main/README.md).

### Gaps

- **Qwen3.5/3.6/3.8-27B, Gemma 4, gpt-oss ve Mistral Small 4 için yayımlanmış Türkçe skor (TurkishMMLU, TR-MMLU, Cetvel, TurkBench) bulunamadı.** Lider tabloları Hugging Face üzerinde; bu oturumda açılamadı.
- Qwen3.6/3.8-27B, Mistral Small 4 / Large 4, DeepSeek V4, GLM-5.x lisansları model kartından doğrulanamadı (yalnız ikincil kaynak).
- Gemma 4 ve Qwen3.8 için bağımsız (satıcı dışı) araç çağırma ölçümü yok; BFCL verisi Aralık 2025'te kalmış.
- Türkçe **el yazısı**, Türkçe **fatura/irsaliye** ve **gürültülü ortam Türkçe konuşma** için bağımsız ölçüm bulunamadı (önceki not da aynı boşluğu yazmıştı).
- Türkçe'ye özel gömme karşılaştırması (ör. Türkçe MTEB) bulunamadı; MTEB çok dilli ortalaması Türkçe'yi ayrı raporlamıyor.
- [DOĞRULANMADI] Türkçe model projeleri (Kumru, Trendyol-LLM yeni sürümleri, Turkcell-LLM, YTÜ Cosmos, T3 AI, TÜBİTAK çalışmaları) için güncel sürüm, lisans ve skor bilgisi açılamadı.

---

## 2. Donanım boyutlandırma ve maliyet (~20–60 kullanıcı; çoğu boşta, birkaçı eşzamanlı); sunum yığınları; işletim yükü; Türkiye/AB'de GPU kiralama

### Takeaway

Hesaplanan yük küçük: 60 kullanıcıda bile ortalama 1'in altında, tepe anında 2–3 eşzamanlı istek. **48–96 GB belleği olan tek NVIDIA GPU'lu bir sunucu** (27–31B model + belge okuma modeli + gömme) bu yükü karşılar; ikinci GPU hız için değil yedeklilik ve model ayrımı için gerekir. Apple silicon ve birleşik bellekli mini istasyonlar tek kullanıcıda iyi, uzun istemli ve eşzamanlı işte belirgin yavaş. **Kasım 2026 satın alma fiyatları bu oturumda doğrulanamadı**; AB'de 48 GB sınıfı GPU saatliği katalogda yaklaşık 0,5–1,7 USD olduğundan, satın almadan önce birkaç haftalık kiralık PoC (yalnız sentetik veriyle) ucuz bir ölçüm yoludur.

### Cited Findings

**2.1 Bağımsız hız ve güç ölçümleri (tek akış; llama.cpp / Ollama)**

Jeff Geerling ölçümleri (üretim hızı token/sn; tepe güç duvar prizinden) [B]:

| Sistem | gpt-oss 20b | DeepSeek R1 14b | Llama 3.1 70b | Tepe güç |
|---|---|---|---|---|
| Masaüstü (Intel 265K) + RTX 4090 24 GB | 279,65 | 92,49 | — (tek 4090'a sığmaz; CPU'ya taşınca 3,10) | 464–509 W |
| Mac Studio M3 Ultra 512 GB | 115,29 | 51,85 | 14,08 | 227–243 W |
| Mac (M1 Ultra, 128 GB) | 100,43 | 41,51 | 9,84 | — |
| Dell Pro Max GB10 (NVIDIA "Spark" sınıfı, 128 GB birleşik) | 87,97 | 23,92 | 4,71 | 123–156 W |
| Framework Desktop (Ryzen AI Max+ 395, 128 GB) | — | 11,37 (CPU) | 4,97 | 133–140 W |
| RTX A4000 16 GB | 93,55 | 36,21 | — | 163–199 W |

- Aynı kaynakta: Mac Studio M3 Ultra 512 GB, DeepSeek R1 671b'yi 19,89 token/sn ile çalıştırıyor (261,8 W); Llama 3.1 405b 1,96 token/sn. — [geerlingguy/ai-benchmarks README](https://raw.githubusercontent.com/geerlingguy/ai-benchmarks/main/README.md)

Llama 3 ile GPU karşılaştırması (Mayıs 2024 anlık görüntüsü; llama.cpp; 1024 token) [B]:

| GPU | 8B Q4 üretim | 8B Q4 **istem işleme** | 70B Q4 üretim | 70B Q4 **istem işleme** |
|---|---|---|---|---|
| RTX 4090 24 GB | 127,74 | 6.898,71 | sığmaz | sığmaz |
| 2 × RTX 4090 | 122,56 | 8.545,00 | 19,06 | 905,38 |
| RTX A6000 48 GB | 102,22 | 3.621,81 | 14,58 | 466,82 |
| RTX 6000 Ada 48 GB | 130,99 | 5.560,94 | 18,36 | 547,03 |
| L40S 48 GB | 113,60 | 5.908,52 | 15,31 | 649,08 |
| A100 80 GB (PCIe) | 138,31 | 5.800,48 | 22,11 | 726,65 |
| H100 80 GB (PCIe) | 144,49 | 7.760,16 | 25,01 | 984,06 |
| Apple M2 Ultra 192 GB | 76,28 | **1.023,89** | 12,13 | **117,76** |
| Apple M3 Max 64 GB | 50,74 | 678,04 | 7,53 | 62,88 |

- Aynı kaynak: Llama 3 8B için Q4_K_M 4,58 GB, F16 14,96 GB; 70B için Q4_K_M 39,59 GB, F16 131,42 GB. Apple silicon'da birleşik belleğin yaklaşık %70'i GPU'ya ayrılabiliyor. — [XiongjieDai/GPU-Benchmarks-on-LLM-Inference](https://raw.githubusercontent.com/XiongjieDai/GPU-Benchmarks-on-LLM-Inference/main/README.md)

**2.2 Sunum yığınları ve OpenAI uyumlu uç noktalar**

| Yığın | Durum (PyPI, 22.11.2026) | OpenAI uyumlu uç | Eşzamanlılık | Dikkat | Kaynak |
|---|---|---|---|---|---|
| **vLLM** | 0.34.0 (16.11.2026), Apache-2.0; son 12 ayda **33 kararlı sürüm** | Evet (`vllm serve`) | Sürekli toplu işleme; araç çağırma ayrıştırıcıları, strict mod, xgrammar/guidance | AWQ, GPTQ, FP8, INT4/INT8, GGUF nicemleme desteği; Linux + NVIDIA odaklı | [PyPI vllm](https://pypi.org/pypi/vllm/json); [vLLM nicemleme](https://raw.githubusercontent.com/vllm-project/vllm/main/docs/features/quantization/README.md) |
| **llama.cpp (llama-server)** | llama-cpp-python 0.3.37 (15.11.2026), MIT | Evet: chat completions, responses, embeddings; ayrıca rerank | "Parallel decoding with multi-user support", sürekli toplu işleme, `-np` yuva sayısı | Prometheus ölçümleri `--metrics`, `--api-key`; çok kipli destek "experimental" | [llama-server README](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/tools/server/README.md); [PyPI](https://pypi.org/pypi/llama-cpp-python/json) |
| **Ollama** | — | Evet (`/v1/chat/completions`, `/v1/responses`) | `OLLAMA_NUM_PARALLEL`: MLX dışındaki arka uçlarda **varsayılan 1**; kuyruk 512 | **Varsayılan bağlam 4096 token** (`OLLAMA_CONTEXT_LENGTH` ile artırılır) | [Ollama SSS](https://raw.githubusercontent.com/ollama/ollama/main/docs/faq.mdx) |
| **SGLang** | 0.5.23 (30.10.2026), Apache-2.0; 12 ayda 18 sürüm | Evet | Yüksek verimli sunum | Qwen README'sinde vLLM ile birlikte öneriliyor | [PyPI sglang](https://pypi.org/pypi/sglang/json); [Qwen3.8 README](https://raw.githubusercontent.com/QwenLM/Qwen3.8/main/README.md) |
| **TGI (Hugging Face)** | **Bakım modunda** | — | — | HF artık vLLM, SGLang, llama.cpp/MLX öneriyor | [TGI README](https://raw.githubusercontent.com/huggingface/text-generation-inference/main/README.md) |
| **MLX-LM (Apple)** | 0.32.2 (16.10.2026), MIT | OpenAI benzeri HTTP | Nicemlenmiş KV önbelleği toplu işlemeyi desteklemiyor | "**not recommended for production** as it only implements basic security checks" | [MLX-LM SERVER.md](https://raw.githubusercontent.com/ml-explore/mlx-lm/main/mlx_lm/SERVER.md) |
| **TEI** (gömme/yeniden sıralama) | — | OpenAI uyumlu `/v1/embeddings` | — | CPU imajı var | [TEI README](https://raw.githubusercontent.com/huggingface/text-embeddings-inference/main/README.md) |

- Ollama yerelde çalışırken veri beyanı: "Ollama runs locally. We don't see your prompts or data when you run locally." — [Ollama SSS](https://raw.githubusercontent.com/ollama/ollama/main/docs/faq.mdx)
- Toplu işlemenin etkisine dair satıcı beyanları [İDDİA, C]: DeepSeek-OCR tek A100-40G'de eşzamanlı PDF işlemede "~2500 tokens/s"; NVIDIA akışlı konuşma modeli tek H100'de 240–2.400 eşzamanlı akış; vLLM v0.6.0, Llama 8B'de (1×H100) önceki sürüme göre 2,7 kat verim. — [DeepSeek-OCR README](https://raw.githubusercontent.com/deepseek-ai/DeepSeek-OCR/main/README.md); [NeMo README](https://raw.githubusercontent.com/NVIDIA/NeMo/main/README.md); [vLLM blog](https://raw.githubusercontent.com/vllm-project/vllm-project.github.io/main/_posts/2024-09-05-perf-update.md)
- Güncelleme temposu (PyPI sürüm geçmişinden sayım; son 12 ay): vLLM 33, llama-cpp-python 21, SGLang 18, MLX-LM 15, PaddleOCR 6, Docling 102, LiteLLM 217 kararlı sürüm [BİRİNCİL]. — [PyPI vllm](https://pypi.org/pypi/vllm/json); [PyPI litellm](https://pypi.org/pypi/litellm/json); [PyPI docling](https://pypi.org/pypi/docling/json)

**2.3 AB'de GPU kiralama (saatlik liste fiyatı; SkyPilot topluluk kataloğu, güncelliği sağlayıcıya göre değişir) [KATALOG]**

| Sağlayıcı / bölge | GPU | USD/saat | 7/24 (730 saat) | Mesai (10 saat × 22 gün = 220 saat) |
|---|---|---|---|---|
| Nebius, eu-north1 (Finlandiya) | L40S 48 GB | 1,5484 | 1.130 USD/ay | 341 USD/ay |
| Nebius, eu-north1 | H100 80 GB | 2,95 | 2.154 USD/ay | 649 USD/ay |
| RunPod (CZ, NL, NO, RO, SE, IS) | RTX A6000 48 GB | 0,49 | 358 USD/ay | 108 USD/ay |
| RunPod (aynı bölgeler) | RTX 4090 24 GB | 0,69 | 504 USD/ay | 152 USD/ay |
| RunPod (aynı bölgeler) | RTX 6000 Ada 48 GB | 0,77 | 562 USD/ay | 169 USD/ay |
| Scaleway, fr-par | H100 80 GB | 2,52 | 1.840 USD/ay | 554 USD/ay |
| Lambda, europe-central-1 | A6000 48 GB | 1,09 | 796 USD/ay | 240 USD/ay |
| OVHcloud, GRA | A100 80 GB | 3,07 | 2.241 USD/ay | 675 USD/ay |

Kaynaklar: [Nebius kataloğu](https://raw.githubusercontent.com/skypilot-org/skypilot-catalog/master/catalogs/v7/nebius/vms.csv); [RunPod](https://raw.githubusercontent.com/skypilot-org/skypilot-catalog/master/catalogs/v7/runpod/vms.csv); [Scaleway](https://raw.githubusercontent.com/skypilot-org/skypilot-catalog/master/catalogs/v7/scaleway/vms.csv); [Lambda](https://raw.githubusercontent.com/skypilot-org/skypilot-catalog/master/catalogs/v7/lambda/vms.csv); [OVHcloud](https://raw.githubusercontent.com/skypilot-org/skypilot-catalog/master/catalogs/v7/ovhcloud/vms.csv). Aylık tutarlar bu notun hesabıdır (saatlik fiyat × saat). Uyarı: katalog tek para birimi sütunu taşır ve USD kabul edildi; Scaleway ve OVHcloud gibi Avrupalı sağlayıcıların liste fiyatı EUR olabilir, sağlayıcı sayfasından teyit edilmeli.

**2.4 AB'de barındırılan açık model uç noktaları (kendi GPU'nu kiralamak yerine token başı; USD / 1M token, girdi / çıktı) [KATALOG]**

| Sağlayıcı | Model | Fiyat |
|---|---|---|
| OVHcloud AI Endpoints | gpt-oss-120b · gpt-oss-20b · Mistral-Small-3.2-24B | 0,08 / 0,40 · 0,04 / 0,15 · 0,09 / 0,28 |
| Scaleway | gpt-oss-120b · gemma-4-26b-a4b · qwen3.6-35b-a3b | 0,15 / 0,60 · 0,25 / 0,50 · 0,25 / 1,50 |
| Nebius | Qwen3.8-27B · gpt-oss-120b | 0,45 / 3,00 · 0,15 / 0,60 |
| Amazon Bedrock, eu-central-1 | gpt-oss-120b · gemma-4-31b · gemma-4-26b-a4b | 0,20 / 0,79 · 0,168 / 0,48 · 0,156 / 0,48 |
| Mistral (La Plateforme) | mistral-small-latest (Small 4) · mistral-large-4 | 0,15 / 0,60 · 0,70 / 2,10 |

Kaynak: [LiteLLM fiyat haritası](https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json) (22.11.2026'da indirildi; Anthropic satırları resmî fiyat sayfasıyla birebir tutuyor, diğer sağlayıcılar resmî sayfadan teyit edilemedi).

### Inferences

- **Yük hesabı (varsayımlar açık).** 60 kullanıcı × 20 etkileşim/gün = 1.200 etkileşim/gün. 9 saatlik güne yayılınca 133/saat = 2,22/dakika. Etkileşim başına model süresi 20 sn varsayılırsa (2 çağrı; her biri ≈1 sn istem işleme + ≈9 sn üretim): 2,22 × 20 / 60 = **0,74 eşzamanlı istek (ortalama)**; tepe katsayısı 3 alınırsa **≈2,2 eşzamanlı**. Belge çıkarma ve zamanlı özetler kuyrukla mesai dışına kaydırılabilir. Sonuç: tek GPU'da 4–8 paralel yuva yeterlidir; darboğaz eşzamanlılık değil, **uzun istemlerin işleme hızı ve bellek**tir.
- **İstem işleme hızı asıl ayırt edici.** Bilgi tabanı sorusu ve araç döngüsü 5–15 bin token girdi taşır. Ölçümlerde NVIDIA kartlar 8B modelde 3.600–7.800 token/sn istem işlerken M2 Ultra 1.024 token/sn'de kalıyor (yaklaşık 3,5–7,5 kat fark); 70B'de fark 4–8 kat. Apple silicon'da 10 bin tokenlık istem saniyelerce bekletir; önbellekli (KV) devam eden sohbetlerde fark azalır (D).
- **Boyut–hız beklentisi (ölçülmedi, enterpolasyon; D):** 24 GB'lık kartta 14B ≈90 token/sn, 2×24 GB'da 70B ≈19 token/sn ölçülmüş; 27–31B yoğun modelin Q4 ile tek 24–32 GB kartta kabaca 35–50 token/sn vermesi beklenir. MoE modeller (gpt-oss-20b, 26B-A4B, 35B-A3B) etkin parametre az olduğundan belirgin hızlıdır (gpt-oss-20b: 4090'da 280 token/sn). **PoC'de gerçek değer ölçülmeli.**
- **Donanım seçenekleri (nitel değerlendirme; fiyatlar doğrulanmadı, bkz. Boşluklar):**

| Seçenek | Ne çalıştırır | Güçlü yanı | Zayıf yanı |
|---|---|---|---|
| A. Tek 24–32 GB GPU'lu iş istasyonu (RTX 4090/5090 sınıfı) | gpt-oss-20b; 27–31B Q4 (15–18 GB) + sınırlı bağlam; OCR ve gömme sıkışık | En ucuz giriş; hızlı | Bellek dar: 8 bit, uzun bağlam ve ikinci model birlikte sığmaz; tüketici kartı, sunucu garantisi yok |
| B. Tek 48 GB GPU'lu sunucu/iş istasyonu (RTX 6000 Ada, L40S, A6000 sınıfı) | 27–31B 8 bit (25–29 GB) + OCR (0,9–3B) + gömme/yeniden sıralama (0,6B) | Üretim için dengeli; ölçümleri mevcut | 120B sınıfı sığmaz; tek kart arızasında yapay zekâ durur (ERP durmaz: DEC-AI-001) |
| C. Tek 96 GB GPU (RTX PRO 6000 Blackwell sınıfı) | B'dekiler + gpt-oss-120b (üretici: tek 80 GB GPU'ya sığar; Q4 hesabı ≈68 GB) veya iki orta model | Tek kartta en geniş seçenek | Fiyat; yine tek arıza noktası |
| D. Çift GPU'lu sunucu (2×48 veya 2×96 GB) | Ana model bir kartta, OCR + gömme + yedek model diğerinde | Yedeklilik; model güncellemesinde kesintisiz geçiş | Güç ≈1 kW tepe, soğutma, kasa; maliyet |
| E. Birleşik bellekli mini istasyon (NVIDIA GB10/"Spark" 128 GB; AMD Ryzen AI Max+ 128 GB) | Büyük modeli yükler ama yavaş (70B: 4,7–5 token/sn) | 125–155 W; sessiz; geliştirme/PoC için uygun | Çok kullanıcılı üretim için yavaş |
| F. Apple silicon (Mac Studio M3 Ultra, 96–512 GB) | Çok büyük modelleri yükler (671B: 19,9 token/sn) | Düşük güç (≈230–260 W); büyük bellek | İstem işleme yavaş; vLLM yok; MLX sunucusu "üretim için önerilmez"; Windows/.NET ağırlıklı ekip için ayrı işletim sistemi |

- **Nicemleme tercihi (D):** Türkçe ek yapısı ve sayı/birim hassasiyeti nedeniyle 4 bit yerine 8 bit/FP8 önce denenmeli; fark şirket setinde ölçülmeli (aynı model Q4 ve 8 bit yan yana). 48 GB kart bu yüzden 24–32 GB karta tercih edilir.
- **İşletim yükü (D, tahmin):** vLLM'in iki haftada bir sürüm çıkarması "her sürümü al" anlamına gelmez; sürüm sabitlenip üç ayda bir, değerlendirme seti yeşilse yükseltilir. Tahmini iş: ilk kurulum 3–5 adam-gün (sürücü, konteyner, izleme, yedek model), sonrası ayda 4–8 saat (güncelleme, model değişimi, disk/ısı izleme). İzleme için llama-server `--metrics` veya vLLM ölçümleri mevcut gözlemlenebilirlik hattına bağlanır (ERP-000L).
- **Güç tüketimi hesabı:** ortalama 0,25 kW'lık tek GPU'lu makine 0,25 × 730 = **182 kWh/ay**; ortalama 0,45 kW'lık çift GPU'lu sunucu **328 kWh/ay**; Mac Studio sınıfı ortalama 0,10 kW ile **73 kWh/ay**. Tutar = kWh × şirketin ticarethane tarifesi (tarife bu oturumda doğrulanamadı; D).
- **"Önce kirala, sonra al" yolu:** RunPod A6000 48 GB mesai saatlerinde ≈108 USD/ay, Nebius L40S ≈341 USD/ay. Dört haftalık PoC maliyeti donanım fiyatının çok küçük bir kesridir; ancak yurt dışı sunucu olduğu için **yalnız sentetik/kişisel veri içermeyen değerlendirme seti** kullanılmalıdır (bkz. Bölüm 5).

### Gaps

- **Kasım 2026 donanım satın alma fiyatları (Türkiye, KDV/gümrük dahil) doğrulanamadı.** [DOĞRULANMADI, eğitim bilgisi; lansman liste fiyatları]: RTX 5090 32 GB ≈1.999 USD (Ocak 2025 lansmanı; 2025–2026'da piyasa fiyatı belirgin üstünde seyretti), RTX PRO 6000 Blackwell 96 GB ≈8.000–9.000 USD, NVIDIA DGX Spark ≈3.999 USD (Ekim 2025 lansmanı), Mac Studio M3 Ultra 96 GB ≈3.999 USD ve 512 GB ≈9.500 USD (Mart 2025 lansmanı). 2026'daki bellek fiyat artışlarının bu rakamları yukarı ittiğine dair haberler vardı; **bu rakamlar teklif yerine geçmez**, yerel tedarikçiden yazılı teklif alınmalıdır.
- RTX 5090 / RTX PRO 6000 Blackwell üzerinde 27–31B modeller için vLLM ile eşzamanlı (4–8 akış) bağımsız ölçüm bulunamadı; eldeki bağımsız ölçümler tek akış ve çoğu 2024–2025 modelleriyle.
- **Türkiye'de GPU kiralama** (telekom ve yerli bulut sağlayıcıları) fiyat ve olgunluk bilgisi açılamadı (bkz. Bölüm 4).
- Hetzner gibi AB'de aylık kiralık GPU'lu fiziksel sunucu fiyatları doğrulanamadı; SkyPilot kataloğundaki bazı satırların (özellikle OVHcloud, Scaleway) güncelliği belirsiz.
- Türkiye ticarethane elektrik tarifesi ve USD/TRY kuru (Kasım 2026) doğrulanamadı; hesaplar bu yüzden kWh ve USD olarak bırakıldı.

---

## 3. Bulut API'leri: Anthropic, OpenAI, Google ve Azure / AWS / Google Cloud üzerinden erişim (fiyat, önbellek/toplu indirim, saklama, eğitimde kullanmama, AB veri yerleşimi, Türkiye'den erişim)

### Takeaway

Anthropic tarafı resmî dokümandan uçtan uca doğrulandı: fiyat aralığı 1M token için 0,10/0,50 USD (Haiku 5.5) ile 8/40 USD (Fable 5.2) arasında; önbellek okuması taban fiyatın %2,5–10'u, toplu işlem %50 indirimli; ticari şartlarda **eğitimde kullanmama** maddesi var; **sıfır saklama (ZDR) satış kanalıyla ve en üst model ailesi hariç** veriliyor; **birinci taraf API'de AB çıkarım bölgesi yok** (yalnız `global` ve `us`), AB'de işleme ancak Amazon Bedrock AB profili veya Google Cloud `eu` çoklu bölgesiyle (+%10) mümkün; Türkiye desteklenen ülkeler listesinde. OpenAI ve Google için güncel fiyatlar iki bağımsız topluluk kataloğunda tutarlı, fakat bu sağlayıcıların ve Azure/AWS'nin **saklama, ZDR ve veri yerleşimi şartları bu oturumda resmî sayfadan doğrulanamadı**.

### Cited Findings

**3.1 Anthropic — resmî fiyat sayfası (USD / 1M token) [BİRİNCİL]**

| Model | Girdi | Çıktı | Önbellek okuma | 5 dk önbellek yazma | Toplu işlem (girdi / çıktı) | Bağlam |
|---|---|---|---|---|---|---|
| Claude Fable 5.2 | 8 | 40 | 0,20 (0,025×) | 10 | 4 / 20 | 1M |
| Claude Opus 5.5 | 4 | 20 | 0,20 (0,05×) | 5 | 2 / 10 | 1M |
| Claude Sonnet 5.5 | 2 | 10 | 0,10 (0,05×) | 2,50 | 1 / 5 | 1M |
| Claude Haiku 5.5 (istem ≤100 bin token) | 0,10 | 0,50 | 0,01 | 0,125 | 0,05 / 0,25 | 1M |
| Claude Haiku 5.5 (istem >100 bin token) | 0,50 | 2,50 | 0,05 | 0,625 | 0,25 / 1,25 | 1M |
| Claude Haiku 4.5 | 1 | 5 | 0,10 | 1,25 | 0,50 / 2,50 | 200K |

- Önceki nesiller hâlâ listede: Opus 5 / 4.8 / 4.7 / 4.6 5/25; Sonnet 5 2/10; Sonnet 4.6 3/15; Fable 5.1 10/50. — [Anthropic fiyatlandırma](https://platform.claude.com/docs/en/about-claude/pricing)
- Önbellek çarpanları: 5 dakikalık yazma 1,25×, 1 saatlik yazma 2×, okuma çoğu modelde 0,1×; "These multipliers stack with other pricing modifiers, including the Batch API discount and data residency." Toplu işlem: "50 percent discount on both input and output tokens." — [Anthropic fiyatlandırma](https://platform.claude.com/docs/en/about-claude/pricing)
- Belirteç sayımı: "Claude 4.7 and later models … use a newer tokenizer … produces approximately 30% more tokens for the same text." — [Anthropic fiyatlandırma](https://platform.claude.com/docs/en/about-claude/pricing)
- PDF maliyeti: "Each page typically uses 1,500–3,000 tokens depending on content density" + "Each page's image uses at most 1,568 visual tokens". — [Anthropic PDF desteği](https://platform.claude.com/docs/en/build-with-claude/pdf-support)
- Ödeme: "All payments are in USD. Credit card and invoicing options are available." Web araması 1.000 arama başına 10 USD. — [Anthropic fiyatlandırma](https://platform.claude.com/docs/en/about-claude/pricing)
- Desteklenen ülkeler listesinde "Türkiye (Turkey)" var; liste hem API hem uygulamalar için tek. — [Anthropic desteklenen ülkeler](https://www.anthropic.com/supported-countries)

**3.2 Anthropic — veri şartları [BİRİNCİL]**

- Ticari Şartlar (yürürlük 17.06.2025): "Anthropic may not train models on Customer Content from Services." Çıktıların sahibi müşteri. Sözleşme tarafı AEA/İsviçre/Birleşik Krallık müşterileri için Anthropic Ireland, Limited; **diğer ülkeler (Türkiye dahil) için Anthropic, PBC** ve Kaliforniya hukuku. — [Anthropic Commercial Terms](https://www.anthropic.com/legal/commercial-terms)
- Veri İşleme Eki (24.02.2025): "Customer is the controller and Anthropic is Customer's processor." AB Standart Sözleşme Maddeleri (Modül 2/3), Birleşik Krallık ve İsviçre ekleri var; **Türkiye'ye özgü aktarım mekanizmasından söz edilmiyor**. İhlal bildirimi "within 48 hours"; AES-256 ve TLS 1.2+; fesihten sonraki 30 gün içinde talep edilirse iade/dışa aktarma, ardından silme; verinin nerede tutulduğu belirtilmiyor. — [Anthropic DPA](https://www.anthropic.com/legal/data-processing-addendum)
- Saklama ilkesi: "Retained data is never used for model training without your express permission." ZDR: "Anthropic does not store customer prompts or responses at rest after the API response is returned"; talep satış ekibinden, kuruluş bazında açılır. — [API ve veri saklama](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)
- **Fable ve Mythos aileleri "Covered Models": 30 gün saklama zorunlu, ZDR altında kullanılamaz.** ZDR'ye uygun olmayan özellikler: toplu işlem (29 gün saklama), Files API, kod çalıştırma, MCP bağlayıcı, mesaj dizileri, Managed Agents. Uygun olanlar: Messages API, önbellek, PDF, alıntılar, yapılandırılmış çıktı (yalnız JSON şeması 24 saate kadar önbellekte). — [API ve veri saklama](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)
- Her düzenlemede istisna: güvenlik sistemlerince işaretlenen içerik "up to 2 years" saklanabilir. — [API ve veri saklama](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)
- Veri yerleşimi: `inference_geo` yalnız `"global"` (varsayılan) ve `"us"` (1,1× fiyat); çalışma alanı coğrafyası yalnız `"us"`. "Current limitations … Inference geo: Only "us" and "global" are available." — [Anthropic veri yerleşimi](https://platform.claude.com/docs/en/manage-claude/data-residency)

**3.3 Claude'a bulut sağlayıcı üzerinden erişim [BİRİNCİL]**

| Kanal | Bölge seçenekleri | Fiyat farkı | Veri işleyen / saklama | Kısıt | Kaynak |
|---|---|---|---|---|---|
| Amazon Bedrock | Global uç; bölgesel uç; coğrafi çıkarım profili (**US, EU, JP, AU**). AB bölgeleri: Frankfurt, Zürih, Stockholm, Milano, İspanya, İrlanda, Londra, Paris | Bölgesel uç +%10 | "runs on AWS-managed infrastructure with zero operator access: Anthropic personnel have no access to the inference infrastructure"; saklamayı Amazon Bedrock yönetir | Toplu işlem, Files, sunucu taraflı araçlar yok; Fable 5.2 yalnız eski `bedrock-runtime` ucunda | [Claude in Amazon Bedrock](https://platform.claude.com/docs/en/build-with-claude/claude-in-amazon-bedrock) |
| Google Cloud (Google yönetimli) | Global; çoklu bölge **`us` veya `eu`**; bölgesel | Çoklu bölge ve bölgesel +%10 | Veri işlemeyi Google Cloud yönetir | Bölgesel uçlar yalnız Sonnet 4.6 ve öncesi; yeni modeller için global veya çoklu bölge | [Claude Models on Google Cloud](https://platform.claude.com/docs/en/build-with-claude/claude-models-on-google-cloud/google-managed) |
| Microsoft Foundry | Global Standard ve **yalnız US** Data Zone Standard | US Data Zone 1,1× | "Anthropic acts as an independent processor for Microsoft"; Azure'da barındırılan dağıtımda istemler Azure altyapısında işlenir | AB veri bölgesi yok; toplu işlem yok | [Claude in Microsoft Foundry](https://platform.claude.com/docs/en/build-with-claude/claude-in-microsoft-foundry) |

- Katalogda Bedrock AB profili fiyatları %10 farkla görünüyor: Opus 5.5 4,40/22; Sonnet 5.5 2,20/11; Haiku 5.5 0,11/0,55; Fable 5.2 8,80/44 [KATALOG]. — [LiteLLM fiyat haritası](https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json)
- Anthropic'in saklama sayfası ayrımı açık yazıyor: Bedrock ve Google yönetimli Vertex'te "the cloud provider is the data processor". — [API ve veri saklama](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)

**3.4 OpenAI (USD / 1M token) — iki topluluk kataloğu tutarlı; resmî sayfa açılamadı [KATALOG]**

| Model | Girdi | Önbellekli girdi | Çıktı | Toplu işlem |
|---|---|---|---|---|
| GPT-6.2 Astra | 8 | 0,40 | 40 | 4 / 20 |
| GPT-6.2 Sol (ve GPT-6.1 Sol) | 2 | 0,10 | 10 | 1 / 5 |
| GPT-6.1 Terra | 1 | 0,05 | 5 | 0,50 / 2,50 |
| GPT-6 Luna | 0,10 | 0,01 | 0,50 | 0,05 / 0,25 |
| GPT-5.6 Sol / GPT-5.5 (eski) | 4 / 5 | 0,40 / 0,50 | 20 / 30 | — |

- 272 bin tokenı aşan istemlerde girdi 2×, çıktı 1,5× (ör. GPT-6.2 Sol 4/15). — [simonw/llm-prices openai.json](https://raw.githubusercontent.com/simonw/llm-prices/main/data/openai.json); [LiteLLM fiyat haritası](https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json)
- GPT-6.1 Terra'nın 26.10.2026'da 1/5 USD ile çıktığı; toplu işlemin %50, otomatik önbelleğin tekrar eden girdide %90'a kadar indirim sağladığı [SNIPPET]. — üçüncü taraf derlemeler: [Puter](https://developer.puter.com/tutorials/openai-api-pricing/); [Morph](https://morphllm.com/openai-api-pricing)
- Azure üzerinden "eu" dağıtım satırları taban fiyatın %10–20 üstünde: ör. azure/eu/gpt-6.2-sol 2,40/12; azure/eu/gpt-6.1-terra 1,20/6; azure/eu/gpt-5.6-sol 4,40/22 [KATALOG]. — [LiteLLM fiyat haritası](https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json)

**3.5 Google Gemini (USD / 1M token) [KATALOG + SNIPPET]**

| Model | Girdi | Çıktı | Not |
|---|---|---|---|
| Gemini 4 Neon | 3 | 15 | Önbellek okuma 0,15 |
| Gemini 4 Hydrogen | 1,5 | 7,5 | Önbellek okuma 0,075 |
| Gemini 4 Argon / Carbon | 2 | 10 | İkinci katalogda 01.12.2026'dan itibaren 4/20 görünüyor |
| Gemini 3.8 Flash | 0,75 | 3,75 | **Tanıtım fiyatı 31.12.2026'ya kadar**; 01.01.2027'den itibaren 1,5/7,5 |
| Gemini 3.5 Flash / Flash-Lite | 1,5 / 0,30 | 9 / 2,5 | — |
| Gemini 3.1 Pro (önizleme) | 2 (≤200 bin) / 4 | 12 / 18 | — |

- Kaynaklar: [LiteLLM fiyat haritası](https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json); [simonw/llm-prices google.json](https://raw.githubusercontent.com/simonw/llm-prices/main/data/google.json)
- Vertex AI sayfası özetlerinde: toplu işlem %50 indirim; Gemini 3.5 Flash global 1,50, global olmayan uçta 1,65 (≈%10 bölgesel fark); arama temellendirmesi ayda 5.000 sorgu ücretsiz, üstü 1.000 sorgu başına 14 USD [SNIPPET]. — [Google Cloud üretken yapay zekâ fiyatları](https://cloud.google.com/vertex-ai/generative-ai/pricing)

**3.6 Mistral (AB merkezli) [KATALOG]:** mistral-small-latest 0,15/0,60; mistral-medium-3.5 1,5/7,5; mistral-large-4 0,70/2,10; Mistral OCR 0,004 USD/sayfa. — [LiteLLM fiyat haritası](https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json)

### Inferences

- **AI-D5 ("saklamayan ve eğitmeyen") şartı Anthropic'te iki ayrı düzeyde karşılanıyor:** eğitmeme, standart ticari şartlarda zaten yazılı (kanıtı sözleşme metni). Saklamama ise varsayılan değil; ZDR için satış ekibiyle ayrı düzenleme gerekir ve Fable/Mythos ailesi, toplu işlem ve Files API bu düzenlemenin dışındadır. Yani "en güçlü model + sıfır saklama" birinci taraf API'de birlikte alınamıyor; ZDR isteniyorsa pratikte Opus 5.5 / Sonnet 5.5 / Haiku 5.5 kalır. KOBİ ölçeğinde ZDR'nin verilip verilmeyeceği satış kanalından sorulmalı (D).
- **"AB bölgesi" Anthropic birinci taraf API'de yok.** AB'de işleme isteniyorsa yol Bedrock AB çıkarım profili veya Vertex `eu` çoklu bölgesidir; bu durumda veri işleyen Anthropic değil AWS/Google olur ve fiyat %10 artar. AB'de işleme KVKK açısından tek başına çözüm değildir (bkz. Bölüm 5).
- **Fiyat kademesi üç sağlayıcıda da benzer hale gelmiş:** "küçük" modeller 0,10/0,50 (Haiku 5.5, GPT-6 Luna), "orta" 1–2/5–10 (Sonnet 5.5, GPT-6.2 Sol, GPT-6.1 Terra, Gemini 4 Hydrogen), "üst" 4–8/20–40. Bu yüzden sağlayıcı seçimi fiyattan çok şirket setindeki kaliteye, sözleşme/KVKK şartlarına ve mevcut adaptörlere (OpenAI ve Anthropic adaptörleri kodda var) göre yapılmalı.
- **Belirteç sayısı model ve dile göre değişir:** Anthropic'in yeni belirteçleyicisi aynı metinde ≈%30 fazla token üretiyor; Türkçe eklemeli yapısı nedeniyle İngilizce'den fazla token tüketebilir (D). Maliyet tahmini gerçek Türkçe örneklerle token sayım ucundan doğrulanmalı.
- [DOĞRULANMADI, eğitim bilgisi; resmî sayfadan teyit edilmeli] OpenAI API'sinde iş verisinin varsayılan olarak eğitimde kullanılmadığı, kötüye kullanım izleme kayıtlarının 30 güne kadar tutulduğu, ZDR'nin onaya bağlı olduğu ve AB veri yerleşimi seçeneğinin bulunduğu; Google'da ücretli katmanda istemlerin ürün geliştirmede kullanılmadığı ve Vertex AI'da saklamasız kullanım için ek adımlar gerektiği; Azure'da kötüye kullanım izleme için 30 güne kadar saklama ve AB veri bölgesi dağıtımı bulunduğu; Amazon Bedrock'ın istem/yanıtları saklamadığı ve model sağlayıcıyla paylaşmadığı biliniyordu.

### Gaps

- OpenAI ve Google **resmî fiyat sayfaları** açılamadı; yukarıdaki tablolar iki topluluk kataloğuna ve arama özetlerine dayanıyor. Gemini 4 Argon/Carbon için iki katalog farklı tarihli fiyat gösteriyor.
- OpenAI, Google (Gemini API ve Vertex AI), Azure ve AWS için **saklama süresi, ZDR koşulu, eğitimde kullanmama metni ve AB veri yerleşimi** resmî sayfalardan doğrulanamadı.
- Anthropic'in ZDR dışındaki **standart ticari saklama süresi** (gizlilik merkezi makalesi) açılamadı; doküman yalnız "Covered Models"te 30 gün ve toplu işlemde 29 gün diyor.
- **Sağlayıcıların KVKK standart sözleşmesini imzalayıp imzalamadığı** bilinmiyor; Anthropic'in ekinde Türkiye mekanizması yok. Azure/AWS/Google'ın Türkiye için ek sözleşme sunup sunmadığı satış/hukuk kanalından sorulmalı.
- Türkiye'den **faturalama** ayrıntıları (yerel fatura, KDV, stopaj, TL ödeme) doğrulanamadı; kardeş not (`docs/delivery/arastirma/2026-11-22_turkiye_yukumlulukler.md`) yurt dışı yazılım/bulut/yapay zekâ ödemelerinde KVK 30 stopajı ve sorumlu sıfatıyla KDV'yi oranı belirsiz olarak işaretlemiş.
- Kullanım kademesi (rate limit) sınırları ve küçük hesaplarda en üst modellere erişim koşulları incelenmedi.

---

## 4. Türkiye'de barındırılan LLM / GPU hizmetleri ve ticari kullanılabilir Türkçe LLM projeleri (olgunluk, fiyat)

### Takeaway

Bu başlıkta **güvenilir, güncel kaynak bulunamadı**: Türkiye'deki telekom ve bulut sağlayıcılarının GPU/LLM hizmetleri, fiyatları ve olgunluğu bu oturumda doğrulanamadı. Elde edilebilen dolaylı kanıt şunlar: büyük sağlayıcıların model hizmetlerinde Türkiye bölgesi görünmüyor; yaygın bir çoklu-sağlayıcı fiyat kataloğunda Türkiye merkezli sağlayıcı yok; yayımlanmış ölçümlerde yerli Türkçe modeller genel modellerin gerisinde. Bu yüzden "Türkiye'de barındırma" seçeneği ancak sağlayıcılardan yazılı teklif ve referans alındıktan sonra değerlendirilebilir.

### Cited Findings

- Claude'un Amazon Bedrock bölge listesinde Türkiye bölgesi yok; en yakınlar Milano (eu-south-1), Frankfurt (eu-central-1), Tel Aviv (il-central-1) ve BAE (me-central-1) [BİRİNCİL]. — [Claude in Amazon Bedrock](https://platform.claude.com/docs/en/build-with-claude/claude-in-amazon-bedrock)
- 161 sağlayıcı ve 7.100 model satırı içeren topluluk fiyat kataloğunda Türkiye merkezli bir sağlayıcı veya Türkiye bölgesi satırı bulunmuyor (22.11.2026) [KATALOG; yokluk kanıtı zayıftır]. — [LiteLLM fiyat haritası](https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json)
- Yayımlanmış ölçümlerde adı geçen yerli/Türkçe modeller ve skorları (2024): Trendyol-LLM 7B (TurkishMMLU 34,1; MMLU-Pro-TR 18,15), Turkcell-LLM-7b-v1 (MMLU-Pro-TR 17,15), YTÜ Cosmos Turkish-Llama-8b (23,90), KOCDIGITAL-LLM-8b (21,83), Orbita (22,95); aynı tabloda taban Meta-Llama-3-8B-Instruct 20,93 [A/B]. — [TurkishMMLU](https://raw.githubusercontent.com/ArdaYueksel/TurkishMMLU/main/README.md); [MMLU-pro-TR](https://raw.githubusercontent.com/bezir/MMLU-pro-TR/main/README.md)
- Cetvel (EACL 2026): Türkçe'ye özel modeller genel çok dilli modellere göre genel olarak daha düşük [SNIPPET, A]. — [EACL 2026 ön baskı](https://preview.aclanthology.org/ingest-eacl/2026.eacl-long.46.pdf)
- Açık kaynak topluluk projesi örneği: Qwen2.5 tabanlı Turkish-LLM-14B (GGUF ile yerelde çalışır); tabana göre kazanım küçük (MMLU_TR +0,30) [C]. — [Turkish-LLM README](https://raw.githubusercontent.com/ogulcanaydogan/Turkish-LLM/main/README.md)
- Eylül 2026 ön baskısı: 7–8B sınıfında Trendyol-8B Türkçe alan belgelerinde %75 ile Qwen2.5-7B'nin (%65) önünde [SNIPPET, C]. — [arXiv 2609.28007](https://arxiv.org/html/2609.28007v1)

### Inferences

- **Türkçe model ≠ Türkiye'de barındırma.** KVKK açısından önemli olan modelin "yerli" olması değil, verinin nerede işlendiğidir. Açık ağırlıklı herhangi bir model şirket sunucusunda çalıştığında veri Türkiye'de kalır; bu, Türkiye'de barındırılan üçüncü taraf hizmete göre daha az sözleşme ve daha fazla kontrol demektir.
- Türkiye'de barındırılan bir GPU/LLM hizmeti ancak şu üç şey yazılı teyit edilirse yerel sunucuya alternatif olur: (i) veri merkezi ve tüm alt işleyenler Türkiye'de, yurt dışından uzaktan erişim yok; (ii) saklamama/eğitmeme taahhüdü; (iii) fiyat ve kapasite (ayrılmış GPU mu, paylaşımlı mı). Aksi hâlde yurt dışı aktarım sorusu yeniden doğar (D).
- [DOĞRULANMADI, eğitim bilgisi] Büyük Türk telekom operatörlerinin GPU bulutu/"yapay zekâ fabrikası" duyuruları ve küresel bir bulut sağlayıcının Türkiye bölgesi için ortaklık duyurusu (2025) biliniyordu; hizmetin Kasım 2026'da kullanılabilir olup olmadığı, fiyatı ve KOBİ'lere açık olup olmadığı doğrulanamadı.

### Gaps

- Türkiye'de GPU kiralama ve barındırılan LLM hizmetleri: sağlayıcı listesi, saatlik/aylık fiyat, asgari taahhüt, SLA, veri merkezi konumu ve sertifikalar **açılamadı**. Önerilen yol: en az üç sağlayıcıdan (telekom + yerli bulut) yazılı teklif ve KVKK/veri konumu beyanı istemek.
- Küresel sağlayıcıların (Google Cloud, Azure, AWS) Türkiye bölgesi takvimi ve hangi yapay zekâ hizmetlerinin o bölgede sunulacağı doğrulanamadı.
- Yerli Türkçe modellerin (Kumru, Trendyol-LLM yeni sürümleri, Turkcell-LLM, YTÜ Cosmos, T3 AI, TÜBİTAK çalışmaları) 2026 sürümleri, lisansları, ticari destek seçenekleri ve güncel skorları doğrulanamadı.

---

## 5. KVKK: 2024 değişikliği sonrası yurt dışına aktarım rejimi, ERP istemlerinde kişisel veri, takma adlandırma, özel nitelikli veri, Kurum'un yapay zekâ rehberliği, kayıt ve şeffaflık, 2026 düzenleme taslakları

> **Bu bölümün tamamı M düzeyindedir.** Kurum (`kvkk.gov.tr`) ve mevzuat sayfaları bu oturumda açılamadı. "Bulgular"da yalnız (a) repodaki kardeş araştırma notunun Kurum sayfalarına atıfla yazdıkları ve (b) bu oturumda okunan sağlayıcı sözleşmeleri var. Madde madde mevzuat özeti "Çıkarımlar"da, [DOĞRULANMADI] işaretiyle verildi. **Hukukçu teyidi olmadan karar dayanağı yapılmamalıdır.**

### Takeaway

Model şirket sunucusunda çalıştığında yurt dışına aktarım doğmaz. Yurt dışındaki bir bulut API'sine (AB bölgesi dahil) kişisel veri giderse bu KVKK m.9 anlamında yurt dışına aktarımdır; yeterlilik kararı bulunmadığı sürece sürekli kullanım için gerçekçi yol **Kurul'un standart sözleşmesinin sağlayıcıyla değiştirilmeden imzalanması ve 5 iş günü içinde Kurum'a bildirilmesidir** — sağlayıcının bunu imzalayıp imzalamayacağı ise belirsizdir. Takma adlandırma riski düşürür fakat aktarım rejimini kendiliğinden kaldırdığı doğrulanamadı; en sağlam tasarım, kişisel verinin buluta hiç çıkmaması ve bunun ölçülüp kayıt altına alınmasıdır.

### Cited Findings

- Kardeş notun Kurum sayfalarına dayanarak yazdığı kural [M]: KVKK m.9 (7499 sayılı Kanun ile değişik) ve 2024/959 sayılı Kurul kararı; standart sözleşme **değiştirilmeden** kullanılır; **imzadan sonra 5 iş günü içinde Kurum'a bildirilir** (modül/KEP); açık rızaya dayalı eski aktarımlar 01.09.2024'e kadar geçerliydi. — `docs/delivery/arastirma/2026-11-22_turkiye_yukumlulukler.md` §8; [KVKK "Yurtdışına Aktarım"](https://www.kvkk.gov.tr/Icerik/2053/Yurtdisina-Aktarim); [KVKK standart sözleşme duyurusu](https://www.kvkk.gov.tr/Icerik/8170/Yurt-Disina-Kisisel-Veri-Aktariminda-Kullanilacak-Standart-Sozlesmelerde-Dikkat-Edilmesi-Gereken-Hususlara-Iliskin-Kamuoyu-Duyurusu)
- Bildirim yapmama idari para cezası 2026 için 90.308–1.806.377 TL [M; ikincil kaynak, tutar kardeş notta "belirsiz" işaretli]. — [Cottgroup bülteni](https://www.cottgroup.com/en/legislation/item/standart-sozlesme-bildirim-yukumlulugu-ve-yaptirimlari)
- Veri ihlalinde Kurul'a 72 saat içinde bildirim (Kurul kararı 2019/10) [M]. — [KVKK "Veri İhlali Bildirimi"](https://www.kvkk.gov.tr/Icerik/5362/Veri-Ihlali-Bildirimi)
- Sağlayıcı tarafı [BİRİNCİL]: Anthropic'in Veri İşleme Eki'nde müşteri veri sorumlusu, Anthropic veri işleyen; aktarım için AB Standart Sözleşme Maddeleri ile Birleşik Krallık ve İsviçre ekleri var, **Türkiye'ye özgü mekanizma yok**; ihlal bildirimi 48 saat içinde. Türkiye'deki müşterinin sözleşme tarafı ABD'deki Anthropic, PBC. — [Anthropic DPA](https://www.anthropic.com/legal/data-processing-addendum); [Anthropic Commercial Terms](https://www.anthropic.com/legal/commercial-terms)
- Birinci taraf Claude API'sinde işleme yeri yalnız `global` veya `us`; Türkiye veya AB seçeneği yok [BİRİNCİL]. — [Anthropic veri yerleşimi](https://platform.claude.com/docs/en/manage-claude/data-residency)
- Sıfır saklama düzenlemesinde bile işaretlenen içerik 2 yıla kadar tutulabilir; en üst model ailesinde 30 gün saklama zorunlu [BİRİNCİL]. — [API ve veri saklama](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)
- Repodaki kilitli kararlar zaten aynı yöne bakıyor: bulut varsayılan kapalı (DEC-AI-004/009); dış yapay zekâya hassas olmayan veri, hassas veri yalnız takma adla ve yöneticinin açtığı türlerde (AI-D3/D4); sağlayıcı saklamayan ve eğitmeyen olmalı (AI-D5); "Çok gizli" sınıf (kimlik no, bordro, erişim anahtarı) hiçbir izinle dışarı çıkmaz. — `docs/decisions/KARAR_SICILI.md`, `docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md`, `docs/security/AI_VERI_GUVENLIK.md`

### Inferences

**Mevzuat özeti (M; [DOĞRULANMADI] — eğitim bilgisine dayanır, Kurum ve mevzuat sayfalarından teyit edilmeli):**

1. **Üç kademeli aktarım rejimi (KVKK m.9, 01.06.2024'ten beri).** Önce m.5/m.6'daki işleme şartlarından biri bulunmalı; sonra sırayla: (a) aktarılan ülke/sektör/uluslararası kuruluş hakkında **yeterlilik kararı**; yoksa (b) **uygun güvence**: Kurul'un ilan ettiği standart sözleşme, Kurul onaylı bağlayıcı şirket kuralları, Kurul izniyle taahhütname veya kamu kurumları arası anlaşma; o da yoksa (c) yalnız **arızi** aktarımlar için sayılı istisnalar (riskler hakkında bilgilendirilmiş açık rıza, ilgili kişiyle sözleşmenin ifası, hakkın tesisi/kullanılması/korunması vb.).
2. **Yeterlilik kararı.** Kurul'un herhangi bir ülke için yeterlilik kararı yayımladığı bilinmiyor (2026 ortası itibarıyla). Bu doğruysa AB veya ABD "yeterli ülke" değildir; **AB bölgesi seçmek aktarım yükümlülüğünü kaldırmaz**, yalnız fiilî riski ve sağlayıcının tabi olduğu hukuku değiştirir.
3. **Standart sözleşme ve bildirim.** Kurul dört tip metin yayımladı (veri sorumlusu→veri sorumlusu, veri sorumlusu→veri işleyen, veri işleyen→veri işleyen, veri işleyen→veri sorumlusu). Metin değiştirilemez, Türkçe imzalanır; imzadan sonra 5 iş günü içinde Kurum'a bildirilir; bildirmeme ayrı bir idari para cezası konusudur. Usul, 10.07.2024 tarihli "Kişisel Verilerin Yurt Dışına Aktarılmasına İlişkin Usul ve Esaslar Hakkında Yönetmelik" ile ve Kurum'un Ocak 2025 tarihli yurt dışına aktarım rehberiyle düzenlendi.
4. **Bağlayıcı şirket kuralları** yalnız çok uluslu bir şirketler topluluğunun kendi içindeki yurt dışı aktarımları içindir; grup dışındaki bir yapay zekâ sağlayıcısına aktarımda çözüm değildir.
5. **Arızi istisnalar sürekli entegrasyona uymaz.** "Arızi", düzensiz, tek seferlik veya birkaç kez yapılan, süreklilik göstermeyen aktarım olarak yorumlanıyor. Her gün çalışan bir asistan arızi sayılamaz; dolayısıyla "çalışandan/müşteriden açık rıza alırız" yaklaşımı 01.09.2024'ten sonra sürekli bulut kullanımının dayanağı olamaz.
6. **Ne aktarım sayılır?** Verinin yurt dışındaki sunucuda işlenmesi veya saklanması, yurt dışından uzaktan erişim ve yurt dışındaki alt işleyenler. Kurul'un 2019/157 sayılı karar özetinde yurt dışı sunuculu e-posta altyapısı kullanımı yurt dışına aktarım sayılmıştı; Kurum'un Kişisel Veri Güvenliği Rehberi'nde bulutta depolama için ayrı tedbir başlığı vardır.
7. **ERP istemlerinde kişisel veri olanlar:** çalışan ve kullanıcı adları, kurumsal e-posta adresleri (ad.soyad biçimi), telefon numaraları; müşteri/tedarikçi **irtibat ve yetkili kişileri**; **şahıs işletmeleri (gerçek kişi tacir)**: unvan çoğu zaman kişinin adıdır ve vergi kimlik numarası gerçek kişinin TCKN/VKN'sidir; gerçek kişiye ait IBAN; sürücü adı ve plaka; e-posta ve PDF gövdelerindeki imzalar. **Kişisel veri olmayanlar:** tüzel kişi unvanı ve vergi numarası, ürün, miktar, fiyat, stok (bunlar ticari sır olarak DEC-AI-004 kapsamında korunur). Tüzel kişiye ait bir bilgi bir gerçek kişiyi belirlenebilir kılıyorsa (tek ortaklı şirket, şahıs firması) kişisel veri gibi ele alınmalıdır.
8. **Takma adlandırma aktarım sorununu kaldırır mı?** KVKK'da takma adlı veri tanımı yoktur. Kurum'un silme/yok etme/anonim hale getirme rehberindeki ölçüt, verinin başka verilerle eşleştirilse bile kişiyle ilişkilendirilememesidir; eşleme anahtarı veri sorumlusunda duruyorsa veri sorumlusu açısından kişisel veri olmaya devam eder. AB'de Adalet Divanı'nın EDPS/SRB kararı (C-413/23 P, Eylül 2025) alıcının yeniden kimliklendirme imkânı yoksa verinin alıcı bakımından kişisel veri sayılmayabileceğini kabul etti; bu Türkiye'de bağlayıcı değildir ve Kurul'un benzer bir kararı bilinmiyor. **Temkinli sonuç:** takma adlandırma tek başına "aktarım yok" demek için yeterli sayılmamalı; ancak dışarı çıkan içerikte doğrudan ve dolaylı tanımlayıcı kalmadığı test ve kayıtla gösterilebiliyorsa "kişisel veri aktarılmıyor" savunması güçlenir. Bu ayrım hukukçu görüşüyle yazılı hale getirilmelidir (repodaki AI-D4 de IBAN'ın takma adla gönderimi için ayrı güvenlik/KVKK incelemesi istiyor).
9. **Özel nitelikli veri (m.6):** sağlık, sendika üyeliği, ceza mahkûmiyeti, biyometrik veri vb. ERP'de İK/bordro ekleri, sağlık raporları, iş kazası kayıtları ve kimlik fotokopilerinde çıkar. Bunlar buluta hiçbir biçimde gitmemeli; yerel modelde de yetki ve amaçla sınırlanmalı.
10. **Otomatik karar (m.11/1-g):** yalnız otomatik analizle kişinin aleyhine sonuç doğmasına itiraz hakkı vardır. Onay önerileri gerçek kişiyi etkilediğinde (şahıs işletmesinin kredi limiti, çalışan talepleri) kararın insanda kalması gerekir; mevcut tasarım (yapay zekâ onay veremez) bununla uyumludur.
11. **Kayıt ve şeffaflık:** (a) aydınlatma metinleri (çalışanlar; müşteri/tedarikçi irtibat kişileri) yapay zekâ ile işleme amacını, alıcı gruplarını ve varsa yurt dışı aktarımı içerecek biçimde güncellenir; (b) kişisel veri işleme envanteri ve kayıt yükümlüsüyse VERBİS kaydı (yurt dışı aktarım alanı dahil) güncellenir; (c) sağlayıcı veri işleyense m.12/2 uyarınca veri güvenliğinden birlikte sorumluluk doğar, yazılı veri işleme sözleşmesi gerekir; (d) sohbet günlükleri, kişisel hafıza ve DEC-AI-005 denetim kayıtları kendileri kişisel veri içerir ve saklama-imha politikasına bağlanmalıdır (plandaki açık kararlardan biri).
12. **Kurum'un yapay zekâ rehberliği:** "Yapay Zekâ Alanında Kişisel Verilerin Korunmasına Dair Tavsiyeler" (Eylül 2021), sohbet robotları hakkında bilgi notu (Kasım 2024) ve "Üretken Yapay Zekâ ve Kişisel Verilerin Korunması Rehberi (15 Soruda)" (Kasım 2025). Ortak temalar: işleme öncesi etki değerlendirmesi, veri minimizasyonu, insan müdahalesi, şeffaflık, çalışanların kurumsal/kişisel veriyi genel amaçlı sohbet robotlarına girmemesi. Başlık ve tarihler teyit edilmeli.
13. **Yapay zekâ düzenleme taslakları:** 2024–2025'te TBMM'ye birkaç milletvekili teklifi sunulmuştu; yasalaşmış bir çerçeve yapay zekâ kanunu bilinmiyor. Kasım 2026 durumu doğrulanamadı. AB Yapay Zekâ Tüzüğü ancak sistemin çıktısı AB'de kullanılırsa gündeme gelir.

**Seçenek bazında KVKK işi (M; hukukçu teyidi gerekir):**

| Seçenek | Yurt dışı aktarım? | Yapılacaklar | Kalan risk |
|---|---|---|---|
| Model şirket sunucusunda | Hayır | Aydınlatma ve envanter güncellemesi, erişim/yetki ve günlük saklama politikası, etki değerlendirmesi | Düşük: iç erişim ve saklama süresi |
| Türkiye'de barındırılan sağlayıcı | Hayır (yurt dışı alt işleyen/erişim yoksa) | Veri işleyen sözleşmesi, konum ve alt işleyen beyanı | Orta: beyanın doğruluğu |
| Yurt dışı bulut, **kişisel veri gidiyor** | **Evet** | Standart sözleşme (sağlayıcı imzalı) + 5 iş günü bildirim + aydınlatma + envanter/VERBİS + veri işleyen sözleşmesi | Yüksek: sağlayıcı imzalamazsa hukuki dayanak kalmaz |
| Yurt dışı bulut, **yalnız kişisel veri içermeyen veya etkin biçimde kimliksizleştirilmiş içerik** | Savunulabilir biçimde hayır | Çıkış filtresi + sızıntı testi + istek kaydı + hukukçu görüşü; serbest metin (e-posta/PDF) dışarı çıkmaz | Orta: filtre kaçırırsa aktarım gerçekleşmiş olur |
| AB'de kiralık GPU'da kendi modelini çalıştırma | Evet (sunucu yurt dışında) | Kişisel veri varsa yukarıdaki standart sözleşme yolu; yoksa yalnız sentetik veriyle PoC | PoC için düşük, üretim için yüksek |

### Gaps

- **Kurum sayfaları açılamadı.** Teyit edilmesi gereken belgeler: 6698 sayılı Kanun m.6, m.9, m.11, m.12, m.18'in güncel metni; 10.07.2024 tarihli Yönetmelik; 2024/959 sayılı Kurul kararı ve dört standart sözleşme metni; Ocak 2025 tarihli yurt dışına aktarım rehberi; standart sözleşme bildirim modülü duyurusu; 2021 tarihli yapay zekâ tavsiyeleri; 2024 sohbet robotları bilgi notu; 2025 üretken yapay zekâ rehberi; 2019/157 sayılı karar özeti; Kişisel Veri Güvenliği Rehberi.
- Kasım 2026 itibarıyla **yeterlilik kararı** çıkıp çıkmadığı doğrulanamadı.
- Kurul'un **takma adlı veri** veya **LLM API'lerine veri gönderimi** hakkında yayımlanmış kararı/görüşü olup olmadığı bilinmiyor.
- **VERBİS** kayıt eşikleri (çalışan sayısı ve bilanço) ve 2026 idari para cezası tutarlarının tamamı doğrulanamadı.
- KVKK'nın AB mevzuatıyla uyum için beklenen kapsamlı değişikliğinin ve yapay zekâ kanunu çalışmalarının **Kasım 2026 durumu** doğrulanamadı.
- Hangi sağlayıcıların **KVKK standart sözleşmesini** imzaladığına dair güvenilir bilgi yok; bu, bulut kararının fiilî ön şartıdır.

---

## 6. Hibrit yönlendirme kalıpları: hangi iş en güçlü modeli ister, hangisi yerelde yeter; takma ad ağ geçidi; yedek yollar

### Takeaway

Eldeki ölçümler aynı ayrımı gösteriyor: **tek adımlı işler (alan çıkarma, sınıflandırma, tek araç çağrısı, gömme, belge ayrıştırma) yerelde yeterli; fark çok adımlı araç kullanımında açılıyor.** Yönlendirme, modelin kararıyla değil sunucuda "görev türü × veri sınıfı" tablosuyla yapılmalı; belge ve e-posta serbest metni her koşulda yerelde kalmalı. Bu ölçekte öğrenen yönlendirici ve üçüncü taraf ağ geçidi fayda değil bakım yükü getirir.

### Cited Findings

- Tek adım ile çok adım farkı (BFCL, ≈Aralık 2025) [B]: tek adımda Qwen3-14B %84,94, Qwen3-32B %88,77, Claude Opus 4.5 %88,58; **çok adımda** Qwen3-14B %34,75, Qwen3-32B %47,87, Claude Opus 4.5 %68,38. — [BFCL verisi](https://raw.githubusercontent.com/ShishirPatil/gorilla/gh-pages/data_overall.csv)
- 2026 yerel modeller için satıcı beyanı [İDDİA, C]: Gemma 4 31B, araç kullanan ajan ölçütü Tau2'de %76,9; Gemma 3 27B %16,2. — [HF "Welcome Gemma 4"](https://raw.githubusercontent.com/huggingface/blog/main/gemma4.md)
- Şema zorlaması biçimi garanti eder, niteliği etmez: "You are guaranteed a validly-parsable function call - not a high-quality one." — [vLLM tool_calling.md](https://raw.githubusercontent.com/vllm-project/vllm/main/docs/features/tool_calling.md)
- Belge ayrıştırmada küçük yerel uzman modeller genel bulut modellerinin üstünde (OmniDocBench v1.6: PaddleOCR-VL-1.6 96,34; Gemini 3 Pro 92,91; GPT-5.2 86,59) [B−]; Türkçe'de satıcı ölçümünde açık model ile Gemini 2.5 Flash eşit (%84,1) [İDDİA]. — [OmniDocBench README](https://raw.githubusercontent.com/opendatalab/OmniDocBench/main/README.md); [Chandra README](https://raw.githubusercontent.com/datalab-to/chandra/master/README.md)
- Kişisel veri tespiti aracı Presidio (MIT; 2.2.366, 27.10.2026): "In its default configuration, it contains recognizers and models for English"; başka dil için NLP motoru ve tanıyıcılar uyarlanmalı; bağlam sözcükleri dile özgü; "Each recognizer can support one language" [BİRİNCİL]. — [Presidio dil desteği](https://raw.githubusercontent.com/microsoft/presidio/main/docs/analyzer/languages.md); [PyPI presidio-analyzer](https://pypi.org/pypi/presidio-analyzer/json)
- Türkçe için hazır spaCy işlem hatları (`tr_core_news_md/lg/trf`) adlandırılmış varlık tanıma bileşeni içeriyor [C]. — [Turkish spaCy models README](https://raw.githubusercontent.com/turkish-nlp-suite/turkish-spacy-models/main/README.md)
- Öğrenen yönlendirici örneği RouteLLM [İDDİA, C]: "reduce costs by up to 85% while maintaining 95% GPT-4 performance" (MT Bench); eşik, Chatbot Arena verisiyle kalibre ediliyor. — [RouteLLM README](https://raw.githubusercontent.com/lm-sys/RouteLLM/main/README.md)
- Üçüncü taraf ağ geçidi örneği LiteLLM: 100+ sağlayıcı, sanal anahtar, harcama takibi, koruma kuralları, karmaşıklığa göre otomatik yönlendirme; son 12 ayda 217 kararlı sürüm [BİRİNCİL]. — [LiteLLM README](https://raw.githubusercontent.com/BerriAI/litellm/main/README.md); [PyPI litellm](https://pypi.org/pypi/litellm/json)
- Sağlayıcı tarafı yedek yol: sunucu taraflı `fallbacks` yalnız Anthropic'in kendi API'sinde; Bedrock, Vertex ve Foundry'de istemci taraflı yedek kalıbı gerekir [BİRİNCİL]. — [Claude in Amazon Bedrock](https://platform.claude.com/docs/en/build-with-claude/claude-in-amazon-bedrock)
- Toplu işlem %50 indirimli ama 29 gün saklamalı ve ZDR dışı [BİRİNCİL]. — [API ve veri saklama](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)
- Repodaki kurallar: hangi yapay zekânın kullanılacağını sunucu seçer, model seçemez (AI-D2); bulut yedeği veri sınıfı izin vermiyorsa otomatik çalışmaz; yapay zekâ kesintisi ERP'yi durdurmaz (DEC-AI-001). — `docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md`, `docs/security/AI_VERI_GUVENLIK.md`, `docs/decisions/KARAR_SICILI.md`

### Inferences

**Önerilen yönlendirme tablosu (yapılandırılabilir; karar değil, ölçümle doğrulanacak):**

| Görev | Varsayılan | Buluta yükseltme (bulut açılmışsa) | Gerekçe |
|---|---|---|---|
| Gömme, yeniden sıralama, indeksleme | Yerel (CPU bile yeter) | Yok | 0,6B sınıfı modeller yeterli; veri hiç çıkmaz |
| OCR ve belge ayrıştırma (e-posta, PDF, irsaliye) | Yerel uzman model + yerel LLM ile alan çıkarma | **Yok** | Belge serbest metni kişisel veri ve ticari sır taşır; küçük uzman modeller ölçümlerde güçlü |
| Sınıflandırma, niyet/yönlendirme, kısa özet | Yerel küçük model | Yok | Tek adım |
| Tek araç çağrısı + kısa cevap ("siparişin durumu") | Yerel | Yok | Tek adımda yerel modeller %85–90 bandında |
| Onay özeti + risk notu (sabit adımlı zincir) | Yerel; sayılar backend calculator'dan | Türkçe üslup/doğruluk eşiği tutmazsa takma adlı yapısal veriyle | İş akışı sabit; model yalnız adım içinde yazar |
| Karar ve modül bilgisi sorusu (kaynaklı) | Yerel | Külliyat kişisel veri içermiyorsa "Şirket içi" sınıfıyla mümkün | Karar belgeleri çoğunlukla kişisel veri içermez |
| Çok adımlı araç kullanımı (modüller arası zincir) | Yerel dene; başarısızsa işi sabit tarife çevir | Takma adlı araç sonuçlarıyla üst model | Ölçümlerde en büyük fark burada |
| Doğal dille rapor (şemalı araçlarla) | Yerel (parametre üretimi) | Karmaşık plan için üst model; yalnız şema ve takma adlı örnek gider | Sayı her zaman backend'den |
| Zamanlı özetler | Yerel, mesai dışı kuyruk | Gerekmez (gecikmeye duyarsız) | Yerel GPU gece boşta |
| Sistem dışı genel/sektörel sohbet | Bulut (şirket verisi yok) veya yerel | — | AI-D1(4), AI-D3 |

**Takma ad ağ geçidi için tasarım noktaları (D; mevcut `AiOutboundTextScreenV1` üzerine):**

1. **Önce yapısal belirteçleme.** ERP hangi alanın kişisel/gizli olduğunu bilir; istem kurulurken cari, kişi, IBAN alanları ana veriden `[CARI_17]`, `[KISI_3]` gibi belirteçlerle yazılır. Bu, serbest metinde sonradan varlık avlamaktan çok daha güvenilirdir.
2. **İkinci ağ: sağlama toplamlı desen tanıyıcılar** (TCKN, VKN, IBAN, telefon, e-posta, plaka). **Üçüncü ağ: Türkçe varlık tanıma.** Presidio kullanılacaksa Türkçe için yapılandırma ve özel tanıyıcı yazmak gerekir; hazır gelmez.
3. **Serbest metin kuralı:** e-posta/PDF gövdesi, onay yorumu, not alanı gibi serbest metin buluta gitmez; gidecekse önce yerel modelle yapısal özete çevrilir.
4. **Eşleme tablosu şirkette kalır**, istek ömürlüdür; cevap sunucuda geri çözülür (AI-D4 ile aynı). Aynı konuşmada belirteç tutarlı olmalı.
5. **Sızıntı testi:** içine bilinen kimlikler gömülmüş sentetik istem seti; hedef sıfır kaçak; her model/istem değişikliğinde koşar. Kaçak görülürse bulut yolu kapalı kalır (fail-closed).
6. **Fayda kaybı:** belirteç anlamı gizler (ör. müşterinin sektörü). Kimlik içermeyen nitelikler (segment, risk sınıfı, vade grubu) belirtecin yanına eklenebilir.

**Yedek yollar (D):** bulut yanıt vermezse → aynı sözleşmeyle yerel model, "sınırlı kalite" işaretiyle; yerel GPU durursa → acil olmayan işler kuyruğa, ERP yapay zekâsız devam eder; **kısıtlı veri sınıfında buluta otomatik düşme yok**; model güncellemesi → sürüm sabitleme + değerlendirme kapısı; sağlayıcının reddettiği istek → kullanıcıya açık mesaj, sessiz model değişimi yok.

### Gaps

- Türkçe metinde kişisel veri tespitinin (ad-soyad, adres, firma adı = kişi adı) doğruluğuna dair bağımsız ölçüm bulunamadı; kendi sızıntı test setimizle ölçülmeli.
- Hibrit yönlendirmenin Türkçe iş yüklerinde kalite/maliyet etkisine dair bağımsız çalışma bulunamadı; RouteLLM rakamları İngilizce sohbet verisine dayanıyor.
- Takma adlı girdinin model kalitesini ne kadar düşürdüğü (özellikle risk notlarında) bilinmiyor; A/B ölçümü gerekir.

---

## 7. Maliyet senaryoları: aylık token hacmi ve yalnız yerel / yalnız bulut / hibrit maliyet (varsayımlar ve hesap açık)

### Takeaway

Varsayılan kullanımda aylık hacim yaklaşık **40–420 milyon girdi ve 3–29 milyon çıktı tokenı**; bunun bulut bedeli orta senaryoda küçük modelle ≈20 USD, orta kademe modelle ≈250–400 USD, en üst kademeyle ≈1.000–1.600 USD/ay. Yerel sunucunun aylık yükü (amortisman + elektrik + bakım) çoğu durumda bundan yüksek ya da benzer çıkar; yani **yereli seçmenin gerekçesi maliyet değil, gizlilik ve kilitli kararlardır**. Hibritte bulut payı ayda yaklaşık 30–360 USD ek getirir.

### Cited Findings

- Hesapta kullanılan birim fiyatlar: Anthropic resmî fiyatları [BİRİNCİL]; OpenAI, Google, Mistral ve AB'de barındırılan açık modeller [KATALOG] (Bölüm 2.4 ve 3). — [Anthropic fiyatlandırma](https://platform.claude.com/docs/en/about-claude/pricing); [LiteLLM fiyat haritası](https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json)
- Belge başına token dayanağı: PDF sayfası 1.500–3.000 metin tokenı + en çok 1.568 görsel token [BİRİNCİL]. — [Anthropic PDF desteği](https://platform.claude.com/docs/en/build-with-claude/pdf-support)
- İndirim kuralları: önbellek okuması 0,05× (Opus/Sonnet 5.5), toplu işlem %50, ikisi birlikte uygulanabilir [BİRİNCİL]. — [Anthropic fiyatlandırma](https://platform.claude.com/docs/en/about-claude/pricing)
- GPU kiralama saat fiyatları [KATALOG]: RunPod A6000 48 GB 0,49 USD; Nebius L40S 48 GB 1,5484 USD. — [RunPod kataloğu](https://raw.githubusercontent.com/skypilot-org/skypilot-catalog/master/catalogs/v7/runpod/vms.csv); [Nebius kataloğu](https://raw.githubusercontent.com/skypilot-org/skypilot-catalog/master/catalogs/v7/nebius/vms.csv)

### Inferences

**Varsayımlar (D; sahibin düzeltmesi gerekir).** Ay 22 iş günü. İş başına token (girdi / çıktı):

| İş | Girdi | Çıktı | Açıklama |
|---|---|---|---|
| (a) Karar/bilgi sorusu | 12.000 | 750 | Sistem + araç tanımı 3.000, getirilen bağlam 4.000, geçmiş 1.000; soru başına ortalama 1,5 model çağrısı |
| (b) Onay özeti + risk notu | 6.000 | 600 | Belge + kurallar + geçmiş; 2 çağrı |
| (c) Belge çıkarma | 13.500 | 1.200 | 3 sayfa × ≈4.000 + 1.500 talimat/şema; çıktı yapısal JSON |
| (d) Zamanlı özet | 10.000 | 800 | Birkaç okuma aracı sonucu |

Günlük adetler: **düşük** (20 kullanıcı): 100 soru, 20 onay, 30 belge, 10 özet · **orta** (40 kullanıcı): 400 soru, 60 onay, 80 belge, 40 özet · **yüksek** (60 kullanıcı): 1.200 soru, 150 onay, 200 belge, 120 özet.

**Aylık hacim (milyon token; adet × 22 × iş başı token):**

| Senaryo | (a) girdi / çıktı | (b) | (c) | (d) | **Toplam girdi / çıktı** |
|---|---|---|---|---|---|
| Düşük | 26,40 / 1,65 | 2,64 / 0,264 | 8,91 / 0,792 | 2,20 / 0,176 | **40,15 / 2,88** |
| Orta | 105,60 / 6,60 | 7,92 / 0,792 | 23,76 / 2,112 | 8,80 / 0,704 | **146,08 / 10,21** |
| Yüksek | 316,80 / 19,80 | 19,80 / 1,98 | 59,40 / 5,28 | 26,40 / 2,112 | **422,40 / 29,17** |

Örnek hesap (orta, (a)): 400 × 22 = 8.800 soru; 8.800 × 12.000 = 105,6 M girdi; 8.800 × 750 = 6,6 M çıktı.

**Yalnız bulut — aylık USD (indirim yok; toplam girdi × girdi fiyatı + toplam çıktı × çıktı fiyatı):**

| Model (girdi / çıktı USD/M) | Düşük | Orta | Yüksek |
|---|---|---|---|
| Claude Haiku 5.5 veya GPT-6 Luna (0,10 / 0,50) | 5,46 | 19,71 | 56,83 |
| Mistral Small 4 (0,15 / 0,60) | 7,75 | 28,04 | 80,86 |
| Gemini 3.8 Flash, tanıtım fiyatı (0,75 / 3,75) | 40,92 | 147,84 | 426,19 |
| GPT-6.1 Terra (1 / 5) | 54,56 | 197,12 | 568,26 |
| Claude Sonnet 5.5 veya GPT-6.2 Sol (2 / 10) | 109,12 | 394,24 | 1.136,52 |
| Gemini 4 Neon (3 / 15) | 163,68 | 591,36 | 1.704,78 |
| Claude Opus 5.5 (4 / 20) | 218,24 | 788,48 | 2.273,04 |
| Claude Fable 5.2 veya GPT-6.2 Astra (8 / 40) | 436,48 | 1.576,96 | 4.546,08 |
| Claude Sonnet 5.5, Bedrock AB profili (2,20 / 11) | 120,03 | 433,66 | 1.250,17 |
| gpt-oss-120b, OVHcloud (0,08 / 0,40) | 4,36 | 15,77 | 45,46 |
| gpt-oss-120b, Bedrock Frankfurt (0,20 / 0,79) | 10,31 | 37,28 | 107,53 |
| Qwen3.8-27B, Nebius (0,45 / 3,00) | 26,71 | 96,36 | 277,60 |

Örnek hesap (orta, Sonnet 5.5): 146,08 × 2 + 10,208 × 10 = 292,16 + 102,08 = **394,24 USD**.

**Önbellek ve toplu işlemle (orta senaryo; varsayım: (a) girdisinin %50'si önbellekten okunur; (c) ve (d) toplu işlemle %50 indirimli):** Sonnet 5.5: 394,24 − 100,32 (52,8 M × (2 − 0,10)) − 46,64 = **247,28 USD**; Opus 5.5: 788,48 − 200,64 − 93,28 = **494,56 USD**; Fable 5.2: 1.576,96 − 411,84 − 186,56 = **978,56 USD**; Haiku 5.5: **12,63 USD**. Not: toplu işlem ve en üst model ailesi sıfır saklama düzenlemesinin dışındadır (Bölüm 3.2).

**Yalnız yerel — aylık yük formülü:** `C / 36 + kWh × tarife + bakım saati × saat ücreti` (C = donanım bedeli, 36 ay amortisman).

- Elektrik: tek GPU'lu makine ≈182 kWh/ay, çift GPU'lu sunucu ≈328 kWh/ay (Bölüm 2).
- Bakım: ayda 4–8 saat (tahmin).
- C doğrulanamadığı için yalnız **örnek bütçe basamakları** (teklif değildir): C = 6.000 USD → 167 USD/ay; C = 15.000 USD → 417 USD/ay; C = 35.000 USD → 972 USD/ay.
- Karşılaştırma için kiralık eşdeğer: 48 GB GPU 7/24 kiralama 358–1.130 USD/ay (RunPod A6000 – Nebius L40S).
- **Başa baş:** yerel ancak `C / 36 <` aylık bulut harcaması ise maliyetle kendini öder. Orta senaryoda Sonnet sınıfı (394,24 USD/ay) için C < 14.193 USD; Opus sınıfı (788,48 USD/ay) için C < 28.385 USD; küçük model sınıfı (19,71 USD/ay) için C < 710 USD — yani küçük modelle yetinilen işte yerel donanım maliyetle hiçbir zaman geri dönmez.

**Hibrit — orta senaryo:** yerel sabit yük (yukarıdaki formül) + buluta giden pay. (a)+(b) hacminin %10 / %20 / %30'u buluta giderse (girdi 11,35 / 22,70 / 34,06 M; çıktı 0,74 / 1,48 / 2,22 M): Sonnet 5.5 ile **30 / 60 / 90 USD**, Opus 5.5 ile **60 / 120 / 181 USD**, Fable 5.2 ile **120 / 241 / 361 USD/ay**.

**Okuma:** (i) Hacim küçük olduğu için bulut bedeli donanım amortismanının altında kalıyor. (ii) Maliyetin yaklaşık %70'i (a) kaleminden geliyor (Sonnet fiyatıyla 277,20 / 394,24 USD); bu kalemde getirilen bağlamı kısaltmak ve önbellek kullanmak en etkili kaldıraç. (iii) Belge çıkarma yerelde kalırsa bulut faturası (c) kadar düşer ve en hassas veri hiç çıkmaz. (iv) Token sayıları model belirteçleyicisine ve Türkçe'ye göre ±%30 oynayabilir.

### Gaps

- Kullanım varsayımları (kişi başı soru, günlük onay ve belge sayısı, sayfa ortalaması) şirket verisiyle doğrulanmadı; mevcut onay kutusu ve gelen belge sayılarından gerçek adetler çıkarılmalı.
- Donanım bedeli (C), elektrik tarifesi, bakım saat ücreti ve kur doğrulanamadı; formül bu yüzden parametrik bırakıldı.
- Türkçe metinde token/kelime oranı ölçülmedi; gerçek örneklerle token sayım ucu kullanılarak ölçülmeli.
- Yerel modelin daha çok deneme/yeniden çalıştırma gerektirmesi hâlinde ortaya çıkacak ek süre maliyeti hesaba katılmadı.

---

## 8. Karar matrisi (yalnız yerel / hibrit / bulut öncelikli) ve seçimden önce yapılması gereken asgari değerlendirme

### Takeaway

Kilitli kararlarla (DEC-AI-004/009) uyumlu iki yol var: **yalnız yerel** ve **hibrit**; "bulut öncelikli" yol karar değişikliği ve en ağır KVKK işini gerektirir. Kalite farkının nerede olduğu ancak şirketin kendi Türkçe soru ve belge setiyle görülebilir; bu yüzden donanım siparişinden ve sağlayıcı sözleşmesinden önce 2–4 haftalık, 150–200 maddelik bir karşılaştırma yapılması asgari şarttır.

### Cited Findings

- Kalite ekseni dayanakları: çok adımlı araç kullanımında yerel–kapalı model farkı (Bölüm 6); belge ayrıştırmada yerel uzman modellerin gücü (Bölüm 1.4); Türkçe'de güncel açık modeller için yayımlanmış skor yokluğu (Bölüm 1.3). — [BFCL verisi](https://raw.githubusercontent.com/ShishirPatil/gorilla/gh-pages/data_overall.csv); [OmniDocBench README](https://raw.githubusercontent.com/opendatalab/OmniDocBench/main/README.md)
- Sağlayıcının kendi tavsiyesi de aynı: "Test with any languages relevant to your specific use cases"; OCR için "collect a dataset of representative examples of your task domain". — [Anthropic çok dillilik](https://platform.claude.com/docs/en/build-with-claude/multilingual-support); [HF OCR yazısı](https://raw.githubusercontent.com/huggingface/blog/main/ocr-open-models.md)
- KVKK ve sözleşme ekseni dayanakları: Bölüm 3.2 ve 5. — [Anthropic DPA](https://www.anthropic.com/legal/data-processing-addendum); [API ve veri saklama](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)
- Bakım ekseni dayanağı: sunum yığınlarının sürüm temposu (vLLM 12 ayda 33 sürüm). — [PyPI vllm](https://pypi.org/pypi/vllm/json)
- Kilitli kararlar: bulut varsayılan kapalı, açılması ayrı sağlayıcı/sözleşme/KVKK/aktivasyon kararı ister. — `docs/decisions/KARAR_SICILI.md` (DEC-AI-004, DEC-AI-009), `docs/decisions/ACIK_KARARLAR.md` (OPEN-014, OPEN-020)

### Inferences

**Karar matrisi (nitel; her hücrenin dayanağı ilgili bölümde):**

| Ölçüt | Yalnız yerel | Hibrit (yerel + sınırlı bulut) | Bulut öncelikli |
|---|---|---|---|
| Kalite: tek adımlı işler, belge okuma, arama | İyi (ölçülecek) | İyi | İyi |
| Kalite: çok adımlı araç kullanımı, uzun bağlam, Türkçe üslup | Belirsiz: 2025 ölçümlerinde geride; 2026 modelleri için yalnız satıcı beyanı var | En iyi (zor iş üst modele) | En iyi |
| Gizlilik ve KVKK işi | En az: aktarım yok | Orta: çıkış filtresi + sızıntı testi + hukukçu görüşü; kişisel veri çıkarsa standart sözleşme | En çok: standart sözleşme ve bildirim şart; sağlayıcı imzası belirsiz |
| Kilitli kararlarla uyum | Tam | Uyumlu (OPEN-020 kapanınca) | Uyumsuz; karar değişikliği ister |
| Yatırım | Donanım (teklif gerekir) | Aynı donanım | Yok |
| Aylık işletme | Elektrik + bakım | Yerel + ≈30–360 USD bulut (orta senaryo) | ≈20–1.600 USD (model kademesine göre) |
| Bakım yükü | Orta–yüksek (GPU, sürücü, model güncelleme) | En yüksek (iki yol + filtre + iki değerlendirme) | Düşük |
| Değere ulaşma süresi | En yavaş (tedarik + kurulum + PoC) | Orta | En hızlı |
| Kesintiye dayanıklılık | İnternetsiz çalışır; tek GPU arıza noktası | En iyi (iki bağımsız yol) | İnternete ve sağlayıcıya bağlı |
| Sağlayıcı bağımlılığı ve fiyat riski | Yok | Düşük | Yüksek (ağ geçidi soyutlamasıyla azalır) |

**Okuma:** "Hibrit" en iyi kaliteyi ve dayanıklılığı verir ama en çok bakım ister. Makul sıra: **önce yalnız yerel ile başlamak, hibrite geçişi ölçüme bağlamak** — yani bulut ancak şu iki koşul birlikte sağlanırsa açılır: (1) asgari değerlendirmede yerel model belirli görev sınıflarında eşiğin altında kalır ve bulut modeli anlamlı farkla geçer; (2) o görev sınıfı kişisel veri çıkmadan (veya standart sözleşmeyle) çalıştırılabilir. Bu bir öneridir; karar OPEN-020 kapsamında sahibindedir.

**Seçimden önce asgari değerlendirme (önceki nottaki değerlendirme CI'ının küçük, tek seferlik öncülü):**

1. **Set (150–200 madde, hepsi şirketin gerçek dili ve belgeleriyle; kişisel veri içerenler sentetiğe çevrilir):**
   - 40 karar/modül bilgisi sorusu (doğru karar numarası ve gerekçe etiketli; 10'u "cevap yok, kaçınmalı" türünde)
   - 20 onay dosyası (özet + risk notu; içine 5 tohumlanmış tutarsızlık)
   - 40 belge: 15 e-posta siparişi/teklif talebi, 15 PDF fatura/teyit, 10 taranmış veya el yazılı irsaliye/kantar fişi; alan düzeyinde doğru değerler
   - 30 araç görevi: 15 tek adım, 15 çok adım (beklenen araç sırası ve parametreler etiketli)
   - 15 doğal dil rapor isteği (beklenen araç parametreleri)
   - 15 güvenlik: belgeye gömülü yönerge, yetkisiz veri isteği
   - Türkçe'ye özgü zorluklar bilerek eklenir: ek almış ürün/cari adları, 1.234,56 sayı biçimi, kg/adet/ton, kısaltmalar (DKP, HRP, ST37), Türkçe–İngilizce karışık e-postalar
2. **Adaylar:** 3 yerel model (ör. Qwen3.8-27B veya 3.6-27B; Gemma 4 31B veya 26B-A4B; gpt-oss-20b), her biri 4 bit ve 8 bit; belge için PaddleOCR tabanlı hat ile görüntü-dil modeli doğrudan okuma karşılaştırması; bulut için 2 model (orta ve üst kademe) **yalnız sentetik/kimliksiz girdilerle**.
3. **Ölçütler:** alan düzeyinde tam eşleşme; araç çağrısında doğru araç + doğru parametre + doğru sıra; aynı görev 3 kez koşulduğunda üçünde de başarı; kaynak gösterme doğruluğu; uydurulmuş sayı adedi (hedef 0); kaçınma doğruluğu; 1, 4 ve 8 eşzamanlı istekte ilk token süresi ve token/sn; görev başına token ve maliyet.
4. **Karar eşikleri önceden yazılır** (ör. belge alan doğruluğu ≥ %95; tek adım araç ≥ %95; çok adım ≥ %80; uydurma sayı = 0). Eşik değerleri işletme politikasıdır; burada önerilmez, yapılandırılabilir bırakılır.
5. **Süre ve kaynak:** 2–4 hafta; kiralık 48 GB GPU (sentetik veriyle) veya ödünç donanım; etiketleme için her modülden birer kullanıcı 1–2 gün.
6. **Çıktı:** görev sınıfı × model tablosu, güven aralığıyla; bu tablo hem donanım boyutunu (hangi model, hangi nicemleme, kaç GB) hem de bulutun hangi görev sınıfı için gerekip gerekmediğini belirler.

### Gaps

- Matristeki "kalite" satırları şirket verisiyle ölçülmedi; bugünkü hâliyle genel benchmark ve satıcı beyanına dayanıyor.
- Değerlendirme setinin etiketleme maliyeti ve kimin yapacağı belirsiz.
- Hibritin gerçek bakım yükü (filtre kaçakları, iki ayrı sözleşme ve iki ayrı sürüm takibi) ancak pilotta görülebilir.

---

## Kapanış (1): Bizim sisteme uyarlama

| Konu | Bulgu (kanıt) | Bizde karşılığı | Uyarlama önerisi (karar değil) |
|---|---|---|---|
| Yerel model adayı | Qwen3.6/3.8-27B, Gemma 4 31B/26B-A4B, gpt-oss-20b/120b ticari kullanıma açık; Türkçe skorları yayımlanmamış (C; lisansların bir kısmı ikincil kaynak) | OPEN-014, OPEN-020 yerel PoC; AI-11 | PoC'de 3 model × 2 nicemleme (4 bit / 8 bit) şirket setiyle ölçülür; model kartından lisans teyidi PoC çıktısına eklenir |
| Sunum yığını | vLLM ve llama-server OpenAI uyumlu uç veriyor; TGI bakım modunda; Ollama varsayılanları dar (4096 bağlam, paralel 1) (BİRİNCİL) | AI-11 ağ geçidi; mevcut OpenAI protokol adaptörü | Yerel model, ağ geçidine "OpenAI uyumlu yerel uç" olarak bağlanır; yeni adaptör yazmak gerekmez. Sürüm sabitlenir, yükseltme değerlendirme kapısından geçer |
| Araç çağırma güvenilirliği | Tek adım yerelde %85–90; çok adımda 2025 açık modelleri geride; şema zorlaması biçimi garanti eder, niteliği etmez (B, BİRİNCİL) | AI-13 okuma araçları, AI-16, AI-24; DEC-AI-003 | Araç şemaları strict tanımlanır; çok adımlı işler sabit tarife çevrilir; çok adım başarısı ayrı ölçülür |
| Belge okuma | Küçük uzman modeller güçlü; PaddleOCR Apache 2.0 ve Türkçe destekli; Surya/Chandra ağırlıkları gelir eşikli lisans (B−/C, BİRİNCİL) | AI-15; DEC-AI-007/008 | Hat: PaddleOCR (PP-OCR / PaddleOCR-VL) → yerel LLM ile şemalı alan çıkarma → deterministik doğrulama. Lisanslı alternatifler ancak ölçüm farkı varsa değerlendirilir |
| Gömme ve yeniden sıralama | 0,6B sınıfı çok dilli modeller yeterli; BGE-M3 yoğun + seyrek birlikte (C) | AI-12 bilgi tabanı | Qwen3-Embedding-0.6B veya BGE-M3 + küçük yeniden sıralayıcı; CPU veya GPU'nun küçük bir dilimi; karar numarası gibi tam eşleşmeler için anahtar kelime araması birlikte kullanılır |
| Konuşma tanıma | Whisper large-v3 güçlü çok dilli taban; Qwen3-ASR Türkçe destekli (B/C) | İlk trende yok (önceki not: sesle yalnız sorgu/taslak) | Ertelenir; gerekirse aynı GPU'da küçük model |
| Donanım | Yük ≈0,7 ortalama, ≈2–3 tepe eşzamanlı; NVIDIA kartlarda istem işleme Apple'a göre 3,5–8 kat hızlı (B + hesap) | OPEN-014 envanteri | Hedef sınıf: 48–96 GB tek NVIDIA GPU; ikinci GPU yedeklilik için opsiyon. Satın almadan önce kiralık GPU'da sentetik veriyle PoC. Fiyat için yerel teklif |
| Bulut sağlayıcı şartı | Eğitmeme sözleşmede var; sıfır saklama ayrı düzenleme ve en üst model ailesi hariç; AB bölgesi yalnız Bedrock/Vertex üzerinden (BİRİNCİL) | OPEN-020; AI-D5, AI-D6 | AI-D5 kanıt paketi üç belgeyle tanımlanır: ticari şartların eğitmeme maddesi, ZDR yazısı (veya saklama süresi kabulü), veri işleme eki. "Sıfır saklama şart mı" sorusu sahibine sorulur |
| KVKK aktarım yolu | Yurt dışı bulut = aktarım; standart sözleşme + 5 iş günü bildirim; sağlayıcı ekinde Türkiye mekanizması yok (M, BİRİNCİL) | OPEN-020; ERP-000J §6 ve §15 | İki yol ayrı ayrı belgelenir: (A) kişisel veri hiç çıkmaz + test kanıtı + hukukçu görüşü; (B) standart sözleşme imzalanır ve bildirilir. Varsayılan A; B ancak sağlayıcı imzalarsa |
| Çıkış filtresi | Yapısal belirteçleme serbest metin taramasından güvenilir; Presidio Türkçe'yi hazır desteklemez (BİRİNCİL + D) | AI-11; `AiOutboundTextScreenV1`; AI-D3/D4 | Filtre sırası: alan sınıfı etiketi → ana veriden belirteç → sağlama toplamlı desenler → Türkçe varlık tanıma. Serbest metin buluta çıkmaz. Sızıntı test seti CI'da |
| Yönlendirme | Görev türü × veri sınıfı tablosu yeterli; öğrenen yönlendirici gereksiz (B/C + D) | AI-D2, AI-D11 | Tablo ayar olarak tutulur (hazırla → onayla → yayınla); model seçimi sunucuda; varsayılan her satırda "yerel" |
| Zamanlı işler | Gecikmeye duyarsız; yerel GPU gece boşta (hesap) | AI-18 | Mesai dışı kuyruk; bulut toplu işlemi gerekmez (ayrıca ZDR dışıdır) |
| Maliyet izleme | Bulut bedeli hacme göre küçük; (a) kalemi ≈%70 (hesap) | AI-11 maliyet/kalite ölçümü; AI-20 | Görev başına token ve süre kaydı ilk günden tutulur; varsayımlar gerçek adetlerle güncellenir |
| Model seçimi yöntemi | Güncel Türkçe skor yok; satıcılar da kendi veriyle test öneriyor (BİRİNCİL) | AI-D11; AI-11, AI-27 | Bölüm 8'deki 150–200 maddelik asgari değerlendirme, donanım siparişi ve sağlayıcı sözleşmesinden önce koşulur |
| Kesinti davranışı | Sunucu taraflı yedek yalnız bazı kanallarda; kısıtlı sınıfta buluta düşme olmamalı (BİRİNCİL + repo kuralı) | DEC-AI-001; AI_VERI_GUVENLIK | Yerel durursa yapay zekâ özellikleri "geçici kapalı" olur; kısıtlı veri sınıfında otomatik bulut yedeği yok |

## Kapanış (2): Önerilmeyenler / fazlalık

1. **Ölçmeden büyük donanım almak** (çift/dört GPU, 100B+ model hedefi): hesaplanan yük tepe anında 2–3 eşzamanlı istek; fazlası atıl kalır.
2. **Trilyon/yüz milyar parametreli amiral açık modelleri (Kimi K3, GLM-5.x, DeepSeek V4, Qwen3.8-2.4T) yerelde çalıştırmayı hedeflemek:** KOBİ donanımının dışında; ayrıca bazılarının lisansı özel.
3. **Türkçe'ye özel küçük ince ayarlı modelleri varsayılan yapmak veya şirket verisiyle ince ayar:** yayımlanmış ölçümler üstünlük göstermiyor; ince ayar AI-D5/AI-D12 ile zaten yasak.
4. **Apple silicon'u çok kullanıcılı üretim sunucusu yapmak:** istem işleme yavaş, MLX sunucusu üretim için önerilmiyor, ekip Windows/.NET ağırlıklı. Geliştirici makinesi olarak sorun yok.
5. **TGI kurmak** (bakım modunda) ve **Ollama'yı varsayılan ayarlarla üretime koymak** (4096 token bağlam, paralel 1).
6. **Surya/Chandra ağırlıklarını lisans almadan ticari kullanmak; MinerU'yu lisans koşullarını okumadan gömmek.**
7. **Öğrenen yönlendirici (RouteLLM türü) ve ek üçüncü taraf ağ geçidi (LiteLLM türü):** mevcut C# ağ geçidi aynı işi görür; ek bileşen yılda yüzlerce sürümle bakım ve tedarik zinciri riski getirir.
8. **"AB bölgesi seçtik, KVKK tamam" varsayımı:** yeterlilik kararı yoksa AB de yurt dışıdır.
9. **Açık rızayı sürekli bulut aktarımının dayanağı yapmak:** açık rıza yalnız arızi aktarım istisnasıdır.
10. **Belge/e-posta serbest metnini takma adlı bile olsa buluta göndermek:** dolaylı tanımlayıcılar kaçar; bu işte yerel modeller zaten güçlü.
11. **Sıfır saklama gereken işte en üst model ailesini veya toplu işlem API'sini seçmek:** ilki 30, ikincisi 29 gün saklama gerektirir.
12. **Yereli maliyet gerekçesiyle seçmek ya da bulutu maliyet gerekçesiyle reddetmek:** bu hacimde maliyet belirleyici değil; eksen gizlilik, kalite ve bakım.
13. **Yurt dışı kiralık GPU'da gerçek şirket verisiyle PoC:** kiralık sunucu da yurt dışı aktarımdır; PoC sentetik veriyle yapılır.
14. **Çin'de barındırılan API uç noktalarına şirket verisi göndermek:** aynı açık ağırlıklar yerelde çalıştırılabilir; uzak uç noktası ayrı bir aktarım ve tedarik riski doğurur.
15. **Lider tablosu sırasına göre model seçmek:** tablolar İngilizce/Çince ağırlıklı ve hızla eskiyor (BFCL verisi Aralık 2025'te kalmış).
16. **Sağlayıcının imzası olmadan "sözleşme var" saymak:** AB standart maddeleri KVKK standart sözleşmesinin yerine geçmez.

## Kapanış (3): Sahibe açık sorular

1. **Bütçe ve yer:** Yerel yapay zekâ sunucusu için bütçe tavanı nedir? Tercih tek 48 GB, tek 96 GB yoksa yedekli çift GPU mu? Sunucu odasında güç, soğutma ve UPS payı var mı (OPEN-014 envanteri)?
2. **Önce kiralık PoC:** Satın almadan önce AB'de kiralık GPU'da, yalnız sentetik veriyle 2–4 haftalık ölçüm yapılmasını onaylıyor musunuz?
3. **Bulutun kapsamı:** Bulut açılırsa yalnız "Genel" ve "Şirket içi" sınıf mı gidecek, yoksa takma adlı "Gizli" veri (fiyat, bakiye) de mi? (AI-D4 veri türü izinleri)
4. **Saklama şartı:** AI-D5'teki "saklamayan" şartı sıfır saklama (ZDR) anlamına mı geliyor, yoksa kısa süreli (ör. 30 gün) saklama kabul edilebilir mi? ZDR şartsa en üst model ailesi ve toplu işlem kullanılamaz.
5. **İşleme yeri:** AB'de işleme tercih mi, şart mı? (Bedrock/Vertex üzerinden +%10 ve farklı veri işleyen anlamına gelir.)
6. **KVKK yolu ve hukukçu:** Hangi hukukçu teyit edecek? Hedef "kişisel veri hiç çıkmaz" yolu mu, standart sözleşme yolu mu? Şirketler VERBİS'e kayıtlı mı; aydınlatma metinlerini kim güncelleyecek?
7. **Şahıs işletmeleri:** Cari kartların ne kadarı şahıs işletmesi? Bunların bilgisi takma adla dahi buluta çıkabilir mi?
8. **Kullanım varsayımları:** Kişi başı günlük soru (5–20), günlük onay (20–150), günlük gelen belge (30–200) ve belge başı sayfa (≈3) varsayımları gerçekçi mi? Gerçek adetler nereden alınabilir?
9. **Yanıt süresi:** Kabul edilebilir bekleme nedir (ör. ilk kelime 2 saniye içinde)? Belge çıkarma ve özetlerin mesai dışında toplu çalışması uygun mu?
10. **Bakım sahibi:** Yapay zekâ sunucusunun bakımını (ayda tahmini 4–8 saat) kim üstlenecek: iç BT mi, dış destek mi?
11. **Değerlendirme seti:** Gerçek soru ve belgeleri kim seçip etiketleyecek? Kişisel veri içeren örneklerin sentetiğe çevrilmesi yeterli mi?
12. **Model menşei:** Çin menşeli açık ağırlıklı modellerin (ör. Qwen) şirket içinde, internete kapalı çalıştırılmasına müşteri/banka sözleşmeleri veya iç politika açısından itiraz var mı?
13. **Ses:** Konuşma tanıma ilk sürümde isteniyor mu, yoksa ertelenebilir mi?
14. **Saklama süreleri:** Sohbet günlükleri, kişisel hafıza ve yapay zekâ denetim kayıtları ne kadar tutulacak; kullanıcı kendi hafızasını silebilecek mi? (Plandaki açık karar.)
15. **Türkiye'de barındırma:** Yerli sağlayıcılardan (telekom / yerli bulut) GPU veya barındırılan model için yazılı teklif ve veri konumu beyanı istenmesini ister misiniz?
