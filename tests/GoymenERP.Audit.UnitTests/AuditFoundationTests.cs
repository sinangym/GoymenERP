using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace GoymenERP.Audit.UnitTests;

public sealed class AuditFoundationTests
{
    [Fact]
    public void AuditDbContext_FinalModel_AppliesCompanyScopedAuditEventFilter()
    {
        var companyId = Guid.NewGuid();
        var companyContext = new CompanyContext();
        companyContext.Set(companyId);
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase($"audit-model-{Guid.NewGuid():N}")
            .EnableServiceProviderCaching(false)
            .Options;
        using var dbContext = new AuditDbContext(options, companyContext);
        var auditEntity = dbContext.Model.FindEntityType(typeof(AuditEvent));

        Assert.NotNull(auditEntity);
        var queryFilter = Assert.Single(auditEntity!.GetDeclaredQueryFilters());
        var expression = Assert.IsAssignableFrom<Expression<Func<AuditEvent, bool>>>(
            queryFilter.Expression);
        AssertCompanyScopedAuditFilter(expression, typeof(AuditDbContext));
        var predicate = expression.Compile();
        Assert.True(predicate(CreateEvent(companyId)));
        Assert.False(predicate(CreateEvent(Guid.NewGuid())));
    }

    [Fact]
    public void CorrelationContext_IsImmutableInsideRequestScope()
    {
        var context = new CorrelationContext();
        context.Set("correlation-1", "cause-1", SourceChannels.Web, "127.0.0.1", "test");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            context.Set("correlation-2", "cause-1", SourceChannels.Web, null, null));

        Assert.Equal("correlation-1", context.CorrelationId);
        Assert.Equal("cause-1", context.CausationId);
        Assert.Contains("değiştirilemez", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditDbContext_RejectsNormalApplicationWrites()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase($"audit-unit-{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new AuditDbContext(options, companyContext);
        dbContext.AuditEvents.Add(CreateEvent(companyContext.CompanyId));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Contains("değiştirilemez", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditEvent_RequiresValidSha256Evidence()
    {
        var data = CreateData(Guid.NewGuid()) with { IntegrityHash = "not-a-hash" };

        var exception = Assert.Throws<ArgumentException>(() => AuditEvent.Create(data));

        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditEvent_RejectsHashThatDoesNotMatchPersistedEnvelope()
    {
        var data = CreateData(Guid.NewGuid()) with { IntegrityHash = new string('B', 64) };

        var exception = Assert.Throws<ArgumentException>(() => AuditEvent.Create(data));

        Assert.Contains("canonical audit zarfıyla eşleşmelidir", exception.Message);
    }

    [Fact]
    public void CanonicalPersistedEnvelope_RecomputesIntegrityHash()
    {
        var auditEvent = AuditEventFactory.Create(new AuditEventDraft(
            Guid.NewGuid(),
            "  test.event.v1  ",
            "  Test.Entity  ",
            $"  {Guid.NewGuid():D}  ",
            "  Update  ",
            1,
            2,
            "  User  ",
            $"  {Guid.NewGuid():D}  ",
            "  test.permission  ",
            [" ROLE_B ", "ROLE_A", "ROLE_B"],
            "  {\"value\":1}  ",
            "  {\"value\":2}  ",
            "  gerekçe  ",
            "  {\"approval\":true}  ",
            "  None  ",
            "  {\"model\":\"test\"}  ",
            "  Web  ",
            "  127.0.0.1  ",
            "  unit-test  ",
            "  correlation-test  ",
            "  causation-test  ",
            "  idempotency-test  "));

        Assert.Equal("test.event.v1", auditEvent.EventType);
        Assert.Equal("gerekçe", auditEvent.Reason);
        Assert.Equal("[\"ROLE_A\",\"ROLE_B\"]", auditEvent.RoleCodesJson);
        Assert.Equal(
            AuditEventIntegrity.Compute(auditEvent.ToIntegrityEnvelope()),
            auditEvent.IntegrityHash);
    }

    private static AuditEvent CreateEvent(Guid companyId)
    {
        return AuditEvent.Create(CreateData(companyId));
    }

    private static void AssertCompanyScopedAuditFilter(
        Expression<Func<AuditEvent, bool>> expression,
        Type contextType)
    {
        var subjectSelection = Assert.IsAssignableFrom<ConditionalExpression>(expression.Body);
        var hasTarget = Assert.IsAssignableFrom<MemberExpression>(subjectSelection.Test);
        Assert.Equal("HasValue", hasTarget.Member.Name);
        var target = Assert.IsAssignableFrom<MemberExpression>(hasTarget.Expression);
        Assert.Equal("groupSharedProductId", target.Member.Name);
        Assert.True(IsCapturedFromContext(target.Expression, contextType));
        var companyBranch = Assert.IsAssignableFrom<BinaryExpression>(subjectSelection.IfFalse);
        Assert.Equal(ExpressionType.AndAlso, companyBranch.NodeType);
        AssertAuditConstantComparison(companyBranch.Left, expression.Parameters[0], nameof(AuditEvent.SubjectKind), "Company");
        var productBranch = Assert.IsAssignableFrom<BinaryExpression>(subjectSelection.IfTrue);
        Assert.Equal(ExpressionType.AndAlso, productBranch.NodeType);
        var productKindAndType = Assert.IsAssignableFrom<BinaryExpression>(productBranch.Left);
        Assert.Equal(ExpressionType.AndAlso, productKindAndType.NodeType);
        AssertAuditConstantComparison(productKindAndType.Left, expression.Parameters[0], nameof(AuditEvent.SubjectKind), "GroupSharedProduct");
        AssertAuditConstantComparison(productKindAndType.Right, expression.Parameters[0], nameof(AuditEvent.EntityType), "Product.Product");
        var targetComparison = Assert.IsAssignableFrom<BinaryExpression>(productBranch.Right);
        Assert.Equal(ExpressionType.Equal, targetComparison.NodeType);
        var entityTarget = Assert.IsAssignableFrom<MemberExpression>(targetComparison.Left);
        Assert.Same(expression.Parameters[0], entityTarget.Expression);
        Assert.Equal(nameof(AuditEvent.EntityId), entityTarget.Member.Name);
        var boundTarget = Assert.IsAssignableFrom<MemberExpression>(targetComparison.Right);
        Assert.Equal("QueryProductId", boundTarget.Member.Name);
        Assert.True(IsCapturedFromContext(boundTarget.Expression, contextType));
        var comparison = Assert.IsAssignableFrom<BinaryExpression>(companyBranch.Right);
        Assert.Equal(ExpressionType.Equal, comparison.NodeType);

        var entityCompanyId = Assert.IsAssignableFrom<MemberExpression>(comparison.Left);
        Assert.Same(expression.Parameters[0], entityCompanyId.Expression);
        Assert.Equal(nameof(AuditEvent.CompanyId), entityCompanyId.Member.Name);

        var contextCompanyId = Assert.IsAssignableFrom<MemberExpression>(comparison.Right);
        Assert.Equal("QueryCompanyId", contextCompanyId.Member.Name);
        Assert.Equal(contextType, contextCompanyId.Member.DeclaringType);
        Assert.True(IsCapturedFromContext(contextCompanyId.Expression, contextType));
    }

    private static void AssertAuditConstantComparison(Expression expression, ParameterExpression parameter, string property, string value)
    {
        var comparison = Assert.IsAssignableFrom<BinaryExpression>(expression);
        Assert.Equal(ExpressionType.Equal, comparison.NodeType);
        var member = Assert.IsAssignableFrom<MemberExpression>(comparison.Left);
        Assert.Same(parameter, member.Expression);
        Assert.Equal(property, member.Member.Name);
        Assert.Equal(value, Assert.IsAssignableFrom<ConstantExpression>(comparison.Right).Value);
    }

    private static bool IsCapturedFromContext(Expression? expression, Type contextType)
    {
        while (expression is MemberExpression memberExpression)
        {
            expression = memberExpression.Expression;
        }

        return expression is ConstantExpression constantExpression
            && contextType.IsAssignableFrom(constantExpression.Type);
    }

    private static AuditEventData CreateData(Guid companyId)
    {
        var envelope = new AuditEventEnvelope(
            Guid.NewGuid(),
            companyId,
            DateTimeOffset.UtcNow,
            "test.event.v1",
            "Test.Entity",
            Guid.NewGuid().ToString("D"),
            "Update",
            1,
            2,
            "User",
            Guid.NewGuid().ToString("D"),
            "test.permission",
            "[\"TEST_ROLE\"]",
            "{\"value\":1}",
            "{\"value\":2}",
            "Birim testi",
            null,
            "None",
            null,
            SourceChannels.Web,
            "127.0.0.1",
            "unit-test",
            "correlation-test",
            "causation-test",
            new string('A', 64),
            "Succeeded",
            1);
        return new AuditEventData(envelope, AuditEventIntegrity.Compute(envelope));
    }
}
