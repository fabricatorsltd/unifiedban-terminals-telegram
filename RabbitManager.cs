/* unified/ban - Management and protection systems

© fabricators SRL, https://fabricators.ltd , https://unifiedban.solutions

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License with our addition
to Section 7 as published in unified/ban's the GitHub repository.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License and the
additional terms along with this program. 
If not, see <https://docs.fabricators.ltd/docs/licenses/unifiedban>.

For more information, see Licensing FAQ: 

https://docs.fabricators.ltd/docs/licenses/faq */

using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Unifiedban.Next.Common.Telegram;

namespace Unifiedban.Next.Terminal.Telegram;

internal class RabbitManager
{
    private static IConnection? _conn;
    private static IModel? _resultsChannel;
    private static IBasicProperties? _resultsProperties;
    private static IModel? _fanoutChannel;
    private static IBasicProperties? _fanoutProperties;

    public void Init()
    {
        Common.Utils.WriteLine("Creating RabbitMQ instance...");

        var factory = new ConnectionFactory();
        factory.UserName = CacheData.Configuration?["RabbitMQ:UserName"];
        factory.Password = CacheData.Configuration?["RabbitMQ:Password"];
        factory.VirtualHost = CacheData.Configuration?["RabbitMQ:VirtualHost"];
        factory.HostName = CacheData.Configuration?["RabbitMQ:HostName"];
        factory.Port = int.Parse(CacheData.Configuration?["RabbitMQ:Port"] ?? "0");
        factory.DispatchConsumersAsync = true;

        Common.Utils.WriteLine("Connecting to RabbitMQ server...");
        _conn = factory.CreateConnection();
    }

    public void Start()
    {
        _resultsChannel = _conn.CreateModel();
        _resultsProperties = _resultsChannel.CreateBasicProperties();
        var resultsConsumer = new AsyncEventingBasicConsumer(_resultsChannel);
        resultsConsumer.Received += ResultsConsumerOnReceived;
        
        _fanoutChannel = _conn.CreateModel();
        _resultsProperties = _fanoutChannel.CreateBasicProperties();
        var fanoutConsumer = new AsyncEventingBasicConsumer(_fanoutChannel);
        fanoutConsumer.Received += FanoutConsumerOnReceived;
        
        Common.Utils.WriteLine("Start consuming queues...");
        _resultsChannel.BasicConsume("tg.results", false, resultsConsumer);
        _fanoutChannel.BasicConsume("tg.terminal.fanout", false, fanoutConsumer);
    }
        
    public void Shutdown()
    {
        _resultsChannel?.Close();
        _conn?.Close();
    }

    private async Task ResultsConsumerOnReceived(object sender, BasicDeliverEventArgs ea)
    {
        try
        {
            var body = ea.Body.ToArray();
            var str = System.Text.Encoding.Default.GetString(body);
            var actionRequest = JsonConvert.DeserializeObject<ActionRequest>(str, new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            MessageQueueManager.EnqueueMessage(actionRequest);
        }
        catch (Exception ex)
        {
            Common.Utils.WriteLine(ex.Message, 3);
            Common.Utils.WriteLine(ex.InnerException?.Message, 3);
        }

        _resultsChannel!.BasicAck(ea.DeliveryTag, false);
        await Task.Yield();
    }

    private async Task FanoutConsumerOnReceived(object sender, BasicDeliverEventArgs ea)
    {
        var body = ea.Body.ToArray();
        var str = System.Text.Encoding.Default.GetString(body);
        
    }
    internal static void PublishMessage(string exchange, string routingKey, byte[] body)
    {
        if (_resultsChannel is { IsClosed: true }) return;

        _resultsChannel.BasicPublish(exchange, routingKey, _resultsProperties, body);
    }
}