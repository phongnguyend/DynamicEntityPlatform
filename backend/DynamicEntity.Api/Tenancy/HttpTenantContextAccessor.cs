using DynamicEntity.Application.Common;
using DynamicEntity.Application.Tenants;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Api.Tenancy;

public sealed class HttpTenantContextAccessor(IHttpContextAccessor httpContextAccessor)
    : ITenantContextAccessor
{
    public const string HeaderName = "X-Tenant-Id";

    public TenantContext GetRequiredTenant()
    {
        var value = httpContextAccessor.HttpContext?.Request.Headers[HeaderName].FirstOrDefault();
        if (!Guid.TryParse(value, out var tenantId))
        {
            throw new ValidationException($"A valid {HeaderName} header is required.");
        }

        return new TenantContext(tenantId);
    }
}
