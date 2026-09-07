using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Alerts;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Api.Tenancy;

/// <summary>
/// Drains the queued alert notifications of every active tenant. It runs independently of
/// <see cref="AlertSchedulerHostedService"/> so a slow SMTP host or webhook never delays evaluation.
/// </summary>
public sealed class AlertNotificationDeliveryHostedService(IServiceScopeFactory scopes,TimeProvider timeProvider,ILogger<AlertNotificationDeliveryHostedService> logger):BackgroundService
{
    private readonly string _owner=$"{Environment.MachineName}:{Guid.NewGuid():N}";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope=scopes.CreateScope();var control=scope.ServiceProvider.GetRequiredService<IControlPlaneStore>();var worker=scope.ServiceProvider.GetRequiredService<AlertNotificationDeliveryWorker>();
                foreach(var tenant in (await control.ListTenantsAsync(stoppingToken)).Where(t=>t.Status==TenantStatus.Active))
                {
                    var delivered=await worker.ProcessTenantAsync(tenant.Id,_owner,stoppingToken);
                    if(delivered>0)logger.LogInformation("Processed {Count} alert notification(s) for tenant {TenantId}.",delivered,tenant.Id);
                }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Alert notification delivery pass failed.");}
            await Task.Delay(TimeSpan.FromSeconds(30),timeProvider,stoppingToken);
        }
    }
}
