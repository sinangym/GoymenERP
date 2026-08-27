namespace GoymenERP.Api.Multitenancy;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequiresCompanyContextAttribute : Attribute;
