using RabbitMQ.Client;

namespace SmartNarrator.Infrastructure.Messaging;

internal static class RabbitMqJobsTopology
{
    internal const string ExchangeName = "smartnarrator.jobs";

    internal const string AiRoutingKey = "jobs.ai";

    internal const string GeneralRoutingKey = "jobs.general";

    internal const string AiQueueName = "smartnarrator.jobs.ai";

    internal const string GeneralQueueName = "smartnarrator.jobs.general";

    internal static void Declare(IModel channel)
    {
        channel.ExchangeDeclare(ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);

        channel.QueueDeclare(AiQueueName, durable: true, exclusive: false, autoDelete: false);
        channel.QueueDeclare(GeneralQueueName, durable: true, exclusive: false, autoDelete: false);

        channel.QueueBind(AiQueueName, ExchangeName, AiRoutingKey);
        channel.QueueBind(GeneralQueueName, ExchangeName, GeneralRoutingKey);
    }
}
