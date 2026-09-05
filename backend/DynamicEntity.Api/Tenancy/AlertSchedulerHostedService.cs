using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Alerts;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Api.Tenancy;

public sealed class AlertSchedulerHostedService(IServiceScopeFactory scopes,TimeProvider timeProvider,ILogger<AlertSchedulerHostedService> logger):BackgroundService
{
    private readonly string _owner=$"{Environment.MachineName}:{Guid.NewGuid():N}";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope=scopes.CreateScope();var control=scope.ServiceProvider.GetRequiredService<IControlPlaneStore>();var worker=scope.ServiceProvider.GetRequiredService<AlertEvaluationWorker>();
                foreach(var tenant in (await control.ListTenantsAsync(stoppingToken)).Where(t=>t.Status==TenantStatus.Active))await worker.ProcessTenantAsync(tenant.Id,_owner,stoppingToken);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Scheduled alert evaluation pass failed.");}
            await Task.Delay(TimeSpan.FromMinutes(1),timeProvider,stoppingToken);
        }
    }
}
