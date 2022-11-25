
namespace Opc.Ua.Cloud.Commander
{
    using Confluent.Kafka;
    using Newtonsoft.Json;
    using Serilog;
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class KafkaClient
    {
        private ApplicationConfiguration _appConfig = null;

        private IProducer<Null, string> _producer = null;
        private IConsumer<Ignore, byte[]> _consumer = null;

        public KafkaClient(ApplicationConfiguration appConfig)
        {
            _appConfig = appConfig;
        }

        public void Connect()
        {
            try
            {
                // create Kafka client
                var config = new ProducerConfig {
                    BootstrapServers = Environment.GetEnvironmentVariable("BROKERNAME") + ":9093",
                    MessageTimeoutMs = 10000,
                    SecurityProtocol = SecurityProtocol.SaslSsl,
                    SaslMechanism = SaslMechanism.Plain,
                    SaslUsername = Environment.GetEnvironmentVariable("USERNAME"),
                    SaslPassword = Environment.GetEnvironmentVariable("PASSWORD")
                };

                _producer = new ProducerBuilder<Null, string>(config).Build();

                var conf = new ConsumerConfig
                {
                    GroupId = Environment.GetEnvironmentVariable("BROKERNAME"),
                    BootstrapServers = Environment.GetEnvironmentVariable("BROKERNAME") + ":9093",
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    SecurityProtocol= SecurityProtocol.SaslSsl,
                    SaslMechanism = SaslMechanism.Plain,
                    SaslUsername = Environment.GetEnvironmentVariable("USERNAME"),
                    SaslPassword= Environment.GetEnvironmentVariable("PASSWORD")
                };

                _consumer = new ConsumerBuilder<Ignore, byte[]>(conf).Build();

                _consumer.Subscribe(Environment.GetEnvironmentVariable("TOPIC"));

                _ = Task.Run(() => HandleCommand());

                Log.Logger.Information("Connected to Kafka broker.");

            }
            catch (Exception ex)
            {
                Log.Logger.Error("Failed to connect to Kafka broker: " + ex.Message);
            }
        }

        public void Publish(string payload)
        {
            Message<Null, string> message = new()
            {
                Headers = new Headers() { { "Content-Type", Encoding.UTF8.GetBytes("application/json") } },
                Value = payload
            };

            _producer.ProduceAsync(Environment.GetEnvironmentVariable("RESPONSE_TOPIC"), message).GetAwaiter().GetResult();
        }

        // handles all incoming commands form the cloud
        private void HandleCommand()
        {
            while (true)
            {
                Thread.Sleep(1000);

                try
                {
                    ConsumeResult<Ignore, byte[]> result = _consumer.Consume();

                    Log.Logger.Information($"Received method call with topic: {result.Topic} and payload: {result.Message.Value}");

                    string requestTopic = Environment.GetEnvironmentVariable("TOPIC");
                    string requestID = result.Topic.Substring(result.Topic.IndexOf("?"));

                    string requestPayload = Encoding.UTF8.GetString(result.Message.Value);
                    string responsePayload = string.Empty;

                    // route this to the right handler
                    if (result.Topic.StartsWith(requestTopic.TrimEnd('#') + "Command"))
                    {
                        new UAClient().ExecuteUACommand(_appConfig, requestPayload);
                        responsePayload = "Success";
                    }
                    else if (result.Topic.StartsWith(requestTopic.TrimEnd('#') + "Read"))
                    {
                        responsePayload = new UAClient().ReadUAVariable(_appConfig, requestPayload);
                    }
                    else if (result.Topic.StartsWith(requestTopic.TrimEnd('#') + "Write"))
                    {
                        new UAClient().WriteUAVariable(_appConfig, requestPayload);
                        responsePayload = "Success";
                    }
                    else
                    {
                        Log.Logger.Error("Unknown command received: " + result.Topic);
                    }

                    // send reponse to Kafka broker
                    Publish(JsonConvert.SerializeObject(responsePayload));
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex, "HandleMessageAsync");

                    // send error to Kafka broker
                    Publish(JsonConvert.SerializeObject(ex.Message));
                }
            }
        }
    }
}