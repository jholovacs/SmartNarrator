using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SmartNarrator.Application.Ports;
using SmartNarrator.Domain.Enums;
using SmartNarrator.Infrastructure.Options;

namespace SmartNarrator.Infrastructure.Messaging;

/// <summary>Publishes durable JSON payloads to the SmartNarrator jobs exchange.</summary>
public sealed class RabbitMqJobPublisher : IJobQueuePublisher, IDisposable
{
    private readonly ILogger<RabbitMqJobPublisher> _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMqJobPublisher(IOptions<RabbitMqConnectionOptions> opts, ILogger<RabbitMqJobPublisher> logger)
    {
        _logger = logger;
        var o = opts.Value;
        var factory = new ConnectionFactory
        {
            HostName = o.HostName,
            Port = o.Port,
            VirtualHost = o.VirtualHost,
            UserName = o.UserName,
            Password = o.Password,
            DispatchConsumersAsync = true,
        };

        _connection = factory.CreateConnection("smartnarrator-api-job-publisher");
        _channel = _connection.CreateModel();
        RabbitMqJobsTopology.Declare(_channel);
    }

    public Task PublishPendingJobAsync(Guid jobId, BackgroundJobType type, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var routingKey = RabbitMqJobRouting.RoutingKey(type);
        var body = JobQueueMessage.Serialize(jobId);

        var props = _channel.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";

        _channel.BasicPublish(RabbitMqJobsTopology.ExchangeName, routingKey, props, body);

        _logger.LogInformation("Published job {JobId} ({Type}) → {RoutingKey}", jobId, type, routingKey);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try
        {
            _channel.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RabbitMQ publish channel dispose failed.");
        }

        try
        {
            _connection.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RabbitMQ publisher connection dispose failed.");
        }
    }
}
