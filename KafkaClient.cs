namespace Opc.Ua.Cloud.Commander
{
    using Confluent.Kafka;
    using Newtonsoft.Json;
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

        public async Task PublishAsync(string payload)
        {
            Message<Null, string> message = new()
            {
                Headers = new Headers() { { "Content-Type", Encoding.UTF8.GetBytes("application/json") } },
                Value = payload
            };

            await _producer.ProduceAsync(Environment.GetEnvironmentVariable("RESPONSE_TOPIC"), message).ConfigureAwait(false);
        }

        // handles all incoming commands form the cloud
        private async Task HandleCommandAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                ResponseModel response = new()
                {
                    TimeStamp = DateTime.UtcNow,
                };

                try
                {
                    ConsumeResult<Ignore, byte[]> result = _consumer.Consume();

                    string requestPayload = Encoding.UTF8.GetString(result.Message.Value);
                    Log.Logger.Information($"Received method call with topic: {result.Topic} and payload: {requestPayload}");

                    // parse the message
                    RequestModel request = JsonConvert.DeserializeObject<RequestModel>(requestPayload);

                    // discard messages that are older than 15 seconds
                    if (request.TimeStamp < DateTime.UtcNow.AddSeconds(-15))
                    {
                        Log.Logger.Information($"Discarding old message with timestamp {request.TimeStamp}");
                        continue;
                    }

                    response.CorrelationId = request.CorrelationId;

                    // route this to the right handler
                    if (request.Command == "MethodCall")
                    {
                        response.Status = await new UAClient().ExecuteUACommandAsync(_appConfig, requestPayload).ConfigureAwait(false);
                        Log.Logger.Information($"Call succeeded, sending response to broker...");
                        response.Success = true;
                    }
                    else if (request.Command == "Read")
                    {
                        response.Status = await new UAClient().ReadUAVariableAsync(_appConfig, requestPayload).ConfigureAwait(false);
                        Log.Logger.Information($"Read succeeded, sending response to broker...");
                        response.Success = true;
                    }
                    else if (request.Command == "HistoricalRead")
                    {
                        response.Status = await new UAClient().ReadUAHistoryAsync(_appConfig, requestPayload).ConfigureAwait(false);
                        Log.Logger.Information($"History read succeeded, sending response to broker...");
                        response.Success = true;
                    }
                    else if (request.Command == "Write")
                    {
                        await new UAClient().WriteUAVariableAsync(_appConfig, requestPayload).ConfigureAwait(false);
                        Log.Logger.Information($"Write succeeded, sending response to broker...");
                        response.Success = true;
                    }
                    else
                    {
                        Log.Logger.Error("Unknown command received: " + request.Command);
                        response.Status = "Unkown command " + request.Command;
                        response.Success = false;
                    }

                    // send reponse to Kafka broker
                    await PublishAsync(JsonConvert.SerializeObject(response)).ConfigureAwait(false);
                    Log.Logger.Information($"Response sent to broker.");
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex, "HandleMessageAsync");
                    response.Status = ex.Message;
                    response.Success = false;

                    // send error to Kafka broker
                    await PublishAsync(JsonConvert.SerializeObject(response)).ConfigureAwait(false);
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