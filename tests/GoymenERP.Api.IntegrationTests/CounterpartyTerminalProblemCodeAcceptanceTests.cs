using System.Reflection;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.AspNetCore.Http;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyTerminalProblemCodeAcceptanceTests
{
    [Fact]
    public void UnknownTerminalResult_CannotBypassProblemCodeThroughSuccessStatus()
    {
        var toHttpResult = typeof(CounterpartyEndpoints).GetMethod(
            "ToHttpResult",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(toHttpResult);

        var resource = new PartyCoreView(
            Guid.NewGuid(),
            "CAR-999999",
            PartyKind.LegalEntity,
            "Bilinmeyen Terminal Sonuç Testi",
            null,
            true,
            1,
            []);
        var unknownResult = (CounterpartyCommandResult)999;
        var responses = new[]
        {
            new CounterpartyCommandResponse(
                unknownResult,
                StatusCodes.Status201Created,
                "Bilinmeyen sonuç.",
                "Stable code olmadan Created dönemez.",
                resource,
                null),
            new CounterpartyCommandResponse(
                unknownResult,
                StatusCodes.Status204NoContent,
                "Bilinmeyen sonuç.",
                "Stable code olmadan NoContent dönemez.",
                null,
                null),
        };

        foreach (var response in responses)
        {
            var invocation = Assert.Throws<TargetInvocationException>(
                () => toHttpResult.Invoke(null, [response]));
            Assert.IsType<InvalidOperationException>(invocation.InnerException);
        }
    }
}
