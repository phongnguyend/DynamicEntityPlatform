using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Metrics;
using DynamicEntity.Application.Analytics;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Application.Alerts;

public sealed class AlertEvaluationWorker(IControlPlaneStore controlPlane,IAlertStore alerts,IAlertEvaluationStore evaluations,
    IAlertNotificationStore notifications,IAlertNotifier notifier,MetricService metrics,AlertThresholdEvaluator thresholdEvaluator,TimeProvider timeProvider)
{
    public async Task<int> ProcessTenantAsync(Guid tenantId,string owner,CancellationToken token)
    {
        var storage=await controlPlane.GetTenantStorageAsync(tenantId,token);if(storage is null)return 0;var count=0;
        while(!token.IsCancellationRequested)
        {
            var now=timeProvider.GetUtcNow();var alert=await alerts.ClaimDueAsync(tenantId,owner,now,now.AddMinutes(2),storage,token);if(alert is null)break;count++;
            using var activity=AnalyticsTelemetry.Source.StartActivity("alert.evaluate");activity?.SetTag("tenant.id",tenantId);activity?.SetTag("entity.id",alert.EntityId);activity?.SetTag("alert.id",alert.Id);activity?.SetTag("metric.id",alert.MetricId);
            try
            {
                var result=await metrics.EvaluateAsync(tenantId,alert.EntityId,alert.MetricId,token);var threshold=JsonSerializer.Deserialize<decimal>(alert.ThresholdJson);
                var firing=thresholdEvaluator.IsMatch(result.Value,threshold,alert.ComparisonOperator);var recovered=alert.LastState==AlertState.Firing&&!firing;
                var evaluation=new AlertEvaluation(Guid.NewGuid(),alert.Id,JsonSerializer.Serialize(result.Value),alert.ThresholdJson,recovered?AlertState.Recovered:firing?AlertState.Firing:AlertState.Normal,null,now);
                await evaluations.CreateAsync(tenantId,evaluation,storage,token);
                var history=await notifications.ListAsync(tenantId,alert.Id,storage,token);var last=history.FirstOrDefault();
                var notify=AlertNotificationPolicy.ShouldNotify(alert.LastState,firing,alert.NotifyOnRecovery,alert.Cooldown,last?.CreatedAt,now);
                activity?.SetTag("alert.firing",firing);activity?.SetTag("alert.notified",notify);
                if(notify)await notifier.NotifyAsync(tenantId,alert,evaluation,storage,token);
                await alerts.CompleteAsync(tenantId,alert with{LastState=firing?AlertState.Firing:AlertState.Normal,LastEvaluatedAt=now,NextEvaluationAt=Next(now,alert.Interval),UpdatedAt=now},storage,token);
            }
            catch(Exception exception) when(exception is not OperationCanceledException)
            {
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error,exception.Message);
                var evaluation=new AlertEvaluation(Guid.NewGuid(),alert.Id,null,alert.ThresholdJson,AlertState.Error,exception.Message,now);
                await evaluations.CreateAsync(tenantId,evaluation,storage,CancellationToken.None);
                await alerts.CompleteAsync(tenantId,alert with{LastState=AlertState.Error,LastEvaluatedAt=now,NextEvaluationAt=Next(now,alert.Interval),UpdatedAt=now},storage,CancellationToken.None);
            }
        }
        return count;
    }
    public static DateTimeOffset Next(DateTimeOffset now,AlertInterval interval)=>interval switch{AlertInterval.FiveMinutes=>now.AddMinutes(5),AlertInterval.Hourly=>now.AddHours(1),AlertInterval.Daily=>now.AddDays(1),_=>throw new ArgumentOutOfRangeException(nameof(interval))};
}
