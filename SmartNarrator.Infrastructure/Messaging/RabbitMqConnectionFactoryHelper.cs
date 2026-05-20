using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SmartNarrator.Infrastructure.Options;

namespace SmartNarrator.Infrastructure.Messaging;

internal static class RabbitMqConnectionFactoryHelper
{
    internal static IConnection CreateConnection(IOptions<RabbitMqConnectionOptions> opts, string clientProvidedName)
    {
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

        return factory.CreateConnection(clientProvidedName);
    }
}
