using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SmartNarrator.Infrastructure.Messaging;
using SmartNarrator.Infrastructure.Options;

namespace SmartNarrator.Infrastructure.Jobs;

/// <summary>Consume AI-heavy jobs (<see cref="Domain.Enums.BackgroundJobType.Analyze"/>, <c>RenderSpeech</c>) one at a time.</summary>
public sealed class AiJobQueueConsumerHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqConnectionOptions> mqOpts,
    ILogger<AiJobQueueConsumerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RabbitMQ AI job consumer starting (prefetch 1).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AI job consumer session crashed; reconnecting shortly.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task RunSessionAsync(CancellationToken stoppingToken)
    {
        using var connection = RabbitMqConnectionFactoryHelper.CreateConnection(mqOpts, "smartnarrator-worker-ai");
        using var channel = connection.CreateModel();

        RabbitMqJobsTopology.Declare(channel);
        channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += (_, ea) => HandleDeliverAsync(channel, ea, stoppingToken);

        channel.BasicConsume(RabbitMqJobsTopology.AiQueueName, autoAck: false, consumer);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task HandleDeliverAsync(IModel channel, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        try
        {
            var jobId = JobQueueMessage.Deserialize(ea.Body);
            await using var scope = scopeFactory.CreateAsyncScope();
            var ran = await BackgroundJobRunner.TryClaimAndExecuteAsync(scope.ServiceProvider, jobId, stoppingToken, logger)
                .ConfigureAwait(false);
            if (!ran)
            {
                logger.LogInformation(
                    "Discarded RabbitMQ job-queue message for job {JobId}: row missing or no longer Pending (deleted/cancelled/in-flight elsewhere).",
                    jobId);
            }

            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            try
            {
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
            catch (Exception nackEx)
            {
                logger.LogTrace(nackEx, "Nack failed during shutdown.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed handling AI queue message.");
            try
            {
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
            catch (Exception nackEx)
            {
                logger.LogTrace(nackEx, "Nack failed.");
            }
        }
    }
}

/// <summary>Consume general jobs (e.g. ingest) with higher parallelism.</summary>
public sealed class GeneralJobQueueConsumerHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqConnectionOptions> mqOpts,
    IOptions<JobsOptions> jobsOpts,
    ILogger<GeneralJobQueueConsumerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var prefetch = Math.Clamp(jobsOpts.Value.RabbitMqGeneralPrefetch, 1, 64);
        logger.LogInformation("RabbitMQ general job consumer starting (prefetch {Prefetch}).", prefetch);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(prefetch, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "General job consumer session crashed; reconnecting shortly.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task RunSessionAsync(int prefetch, CancellationToken stoppingToken)
    {
        using var connection = RabbitMqConnectionFactoryHelper.CreateConnection(mqOpts, "smartnarrator-worker-general");
        using var channel = connection.CreateModel();

        RabbitMqJobsTopology.Declare(channel);
        channel.BasicQos(0, (ushort)Math.Clamp(prefetch, 1, 256), global: false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += (_, ea) => HandleDeliverAsync(channel, ea, stoppingToken);

        channel.BasicConsume(RabbitMqJobsTopology.GeneralQueueName, autoAck: false, consumer);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task HandleDeliverAsync(IModel channel, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        try
        {
            var jobId = JobQueueMessage.Deserialize(ea.Body);
            await using var scope = scopeFactory.CreateAsyncScope();
            var ran = await BackgroundJobRunner.TryClaimAndExecuteAsync(scope.ServiceProvider, jobId, stoppingToken, logger)
                .ConfigureAwait(false);
            if (!ran)
            {
                logger.LogInformation(
                    "Discarded RabbitMQ job-queue message for job {JobId}: row missing or no longer Pending (deleted/cancelled/in-flight elsewhere).",
                    jobId);
            }

            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            try
            {
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
            catch (Exception nackEx)
            {
                logger.LogTrace(nackEx, "Nack failed during shutdown.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed handling general queue message.");
            try
            {
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
            catch (Exception nackEx)
            {
                logger.LogTrace(nackEx, "Nack failed.");
            }
        }
    }
}
