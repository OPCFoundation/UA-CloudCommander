namespace Opc.Ua.Cloud.Commander
{
    using Confluent.Kafka;
    using Serilog;
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class KafkaClient : IDisposable
    {
        private ApplicationConfiguration _appConfig = null;

        private IProducer<Null, string> _producer = null;
        private IConsumer<Ignore, byte[]> _consumer = null;
        private CancellationTokenSource _cts = new();

        public KafkaClient(ApplicationConfiguration appConfig)
        {
            _appConfig = appConfig;
        }

        public void Connect()
        {
            try
            {
                // delete old producer/consumer instances if they exist
                _consumer?.Close();   // commits offsets, leaves consumer group
                _consumer?.Dispose();
                _producer?.Flush(TimeSpan.FromSeconds(5));
                _producer?.Dispose();

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
                    GroupId = Environment.GetEnvironmentVariable("CLIENTNAME"),
                    BootstrapServers = Environment.GetEnvironmentVariable("BROKERNAME") + ":9093",
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    SecurityProtocol= SecurityProtocol.SaslSsl,
                    SaslMechanism = SaslMechanism.Plain,
                    SaslUsername = Environment.GetEnvironmentVariable("USERNAME"),
                    SaslPassword= Environment.GetEnvironmentVariable("PASSWORD")
                };

                _consumer = new ConsumerBuilder<Ignore, byte[]>(conf).Build();

                _consumer.Subscribe(Environment.GetEnvironmentVariable("TOPIC"));

                _ = Task.Run(async () => await HandleCommandAsync().ConfigureAwait(false));

                Log.Logger.Information("Connected to Kafka broker.");

            }
            catch (Exception ex)
            {
                Log.Logger.Error("Failed to connect to Kafka broker: " + ex.Message);
            }
        }

        public async Task PublishAsync(string topic, string payload)
        {
            Message<Null, string> message = new()
            {
                Headers = new Headers() { { "Content-Type", Encoding.UTF8.GetBytes("application/json") } },
                Value = payload
            };

            await _producer.ProduceAsync(topic, message).ConfigureAwait(false);
        }

        // handles all incoming commands form the cloud
        private async Task HandleCommandAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    ConsumeResult<Ignore, byte[]> result = _consumer.Consume();

                    string requestPayload = Encoding.UTF8.GetString(result.Message.Value);
                    Log.Logger.Information($"Received command with topic: {result.Topic} and payload: {requestPayload}");

                    // execute the spec-compliant OPC UA PubSub ActionRequest and build the ActionResponse
                    PubSubActionResult actionResult = await PubSubActionHandler.ProcessRequestAsync(_appConfig, requestPayload).ConfigureAwait(false);
                    if (!actionResult.ShouldRespond)
                    {
                        continue;
                    }

                    // the Requestor's ResponseAddress takes precedence over the configured response topic
                    string responseTopic = actionResult.ResponseAddress ?? Environment.GetEnvironmentVariable("RESPONSE_TOPIC");

                    // send the ActionResponse NetworkMessage to the Kafka broker
                    await PublishAsync(responseTopic, actionResult.ResponseJson).ConfigureAwait(false);
                    Log.Logger.Information("Response sent to broker.");
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex, "HandleCommandAsync");
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _consumer?.Close();   // commits offsets, leaves consumer group
            _consumer?.Dispose();
            _producer?.Flush(TimeSpan.FromSeconds(5));
            _producer?.Dispose();
            _cts.Dispose();
        }
    }
}