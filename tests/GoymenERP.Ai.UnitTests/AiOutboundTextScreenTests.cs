using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Ai.UnitTests;

// SINAN_KARARLARI_2026-11-03_AI AI-D4 implementation note: a free-text question holding a sensitive value is blocked
// before an external call. Synthetic values only (SENTETIK).
public sealed class AiOutboundTextScreenTests
{
    [Theory]
    [InlineData("SENTETIK stok listesindeki aktif urunleri goster")]
    [InlineData("Bu hafta kac siparis taslagi var?")]
    [InlineData("SENTETIK-depo-2 icin sayim durumu nedir")]
    public void A_general_question_without_sensitive_values_is_clean(string question)
    {
        var result = AiOutboundTextScreenV1.Screen(question);

        Assert.True(result.IsClean);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("TR00 0000 0000 0000 0000 0000 00 hesabi kimin", AiOutboundTextFindingV1.Iban)]
    [InlineData("sentetik@example.invalid adresine bak", AiOutboundTextFindingV1.EmailAddress)]
    [InlineData("0 500 000 00 00 numarali kisi", AiOutboundTextFindingV1.DigitSequence)]
    [InlineData("fiyati 1.250,00 TL olan urun", AiOutboundTextFindingV1.CurrencyAmount)]
    [InlineData("bakiyesi ₺300 olan cari", AiOutboundTextFindingV1.CurrencyAmount)]
    [InlineData("tutar 75 EUR mi", AiOutboundTextFindingV1.CurrencyAmount)]
    public void A_sensitive_value_in_the_question_is_detected_by_kind(string question, AiOutboundTextFindingV1 expected)
    {
        var result = AiOutboundTextScreenV1.Screen(question);

        Assert.False(result.IsClean);
        Assert.Contains(expected, result.Findings);
    }

    [Fact]
    public void The_result_names_only_the_kind_never_the_matched_value()
    {
        const string question = "sentetik@example.invalid ve TR00 0000 0000 0000 0000 0000 00";

        var result = AiOutboundTextScreenV1.Screen(question);

        Assert.DoesNotContain("example.invalid", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("TR00", result.ToString(), StringComparison.Ordinal);
        Assert.Contains(AiOutboundTextFindingV1.EmailAddress, result.Findings);
        Assert.Contains(AiOutboundTextFindingV1.Iban, result.Findings);
    }

    [Fact]
    public void A_word_ending_in_a_currency_like_suffix_is_not_an_amount()
    {
        Assert.True(AiOutboundTextScreenV1.Screen("SENTETIK 3 TLX parcasi").IsClean);
    }
}

/// <summary>AI-D44: a free question is Internal unless its text carries personal data. All texts are SENTETIK.</summary>
public sealed class AiQuestionDataClassTests
{
    [Theory]
    [InlineData("SENTETIK: bugün bekleyen sevkiyatlar hangileri?", GoymenERP.BuildingBlocks.Ai.AiDataClassV1.Internal)]
    [InlineData("SENTETIK: 1.250 TL tutarlı sipariş nerede?", GoymenERP.BuildingBlocks.Ai.AiDataClassV1.Internal)]
    [InlineData("SENTETIK: TCKN 10000000146 olan müşteri kim?", GoymenERP.BuildingBlocks.Ai.AiDataClassV1.Confidential)]
    [InlineData("SENTETIK: 0555 000 00 00 numaralı kişi", GoymenERP.BuildingBlocks.Ai.AiDataClassV1.Confidential)]
    [InlineData("SENTETIK: TR00 0000 0000 0000 0000 0000 00 hesabı", GoymenERP.BuildingBlocks.Ai.AiDataClassV1.Confidential)]
    [InlineData("SENTETIK: sentetik@example.com adresli kişi", GoymenERP.BuildingBlocks.Ai.AiDataClassV1.Confidential)]
    public void A_question_is_internal_unless_it_carries_personal_data(string question, GoymenERP.BuildingBlocks.Ai.AiDataClassV1 expected)
    {
        Assert.Equal(expected, GoymenERP.BuildingBlocks.Ai.AiQuestionDataClassV1.Classify(question));
    }
}
