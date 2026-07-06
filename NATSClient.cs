namespace Opc.Ua.Cloud.Commander
{
    using NATS.Client.Core;
    using Serilog;
    using System;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class NATSClient
    {
        private readonly ApplicationConfiguration _uaApplication;
        private NatsConnection _conn;
        private CancellationTokenSource _cts = new();

        public NATSClient(ApplicationConfiguration uaApplication)
        {
            _uaApplication = uaApplication;
        }

        public async Task ConnectAsync()
        {
            try
            {
                var host = Environment.GetEnvironmentVariable("BROKERNAME");
                var port = Environment.GetEnvironmentVariable("BROKERPORT") ?? "4222";
                string natsUrl = $"nats://{host}:{port}";

                var topic = Environment.GetEnvironmentVariable("TOPIC");
                var responseTopic = Environment.GetEnvironmentVariable("RESPONSE_TOPIC");
                if (string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(responseTopic))
                {
                    Log.Logger.Error("TOPIC or RESPONSE_TOPIC not configured. Cannot connect NATS transport.");
                    return;
                }

                // Convert MQTT topic filter to NATS subject filter.
                var subjectFilter = MqttTopicToNatsSubject(topic);

                // Dispose previous connection if any.
                if (_conn != null)
                {
                    _cts.Cancel();
                    await _conn.DisposeAsync().ConfigureAwait(false);
                    _cts.Dispose();
                    _cts = new CancellationTokenSource();
                    _conn = null;
                }

                bool useAuth = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USERNAME")) &&
                               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PASSWORD"));
                bool useTls = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USE_TLS"));
                bool useUaCertAuth = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USE_UA_CERT_AUTH"));

                NatsTlsOpts tlsOpts = null;
                if (useTls)
                {
                    tlsOpts = new NatsTlsOpts() {
                        ConfigureClientAuthentication = ssl =>
                        {
                            if (useUaCertAuth)
                            {
                                ssl.ClientCertificates = new X509CertificateCollection {
                                    _uaApplication.SecurityConfiguration.ApplicationCertificate.Certificate
                                };
                            }

                            return ValueTask.CompletedTask;
                        }
                    };
                }

                var opts = new NatsOpts
                {
                    Url = natsUrl,
                    Name = Environment.GetEnvironmentVariable("CLIENTNAME") ?? null,
                    AuthOpts = useAuth ? new NatsAuthOpts { Username = Environment.GetEnvironmentVariable("USERNAME"), Password = Environment.GetEnvironmentVariable("PASSWORD") } : null,
                    TlsOpts  = tlsOpts
                };

                _conn = new NatsConnection(opts);

                _ = Task.Run(async () => await HandleCommand(subjectFilter, responseTopic, _cts.Token).ConfigureAwait(false));

                Log.Logger.Information("Connected to NATS broker and subscribed on {Subject}", subjectFilter);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Failed to connect to NATS broker");
            }
        }

        private async Task HandleCommand(string subjectFilter, string responseSubjectBase, CancellationToken ct)
        {
            if (_conn == null) return;

            // Subscribe and process messages
            await foreach (var msg in _conn.SubscribeAsync<string>(subjectFilter, cancellationToken: ct).ConfigureAwait(false))
            {
                await HandleCommandAsync(msg.Subject, msg.Data, responseSubjectBase, ct).ConfigureAwait(false);
            }
        }

        private async Task HandleCommandAsync(string subject, string payload, string responseSubjectBase, CancellationToken ct)
        {
            Log.Logger.Information("Received cloud command with subject: {Subject} and payload: {Payload}", subject, payload);

            try
            {
                // execute the spec-compliant OPC UA PubSub ActionRequest and build the ActionResponse
                PubSubActionResult actionResult = await PubSubActionHandler.ProcessRequestAsync(_uaApplication, payload).ConfigureAwait(false);
                if (!actionResult.ShouldRespond)
                {
                    return;
                }

                // the Requestor's ResponseAddress takes precedence over the configured response subject
                string responseSubject = MqttTopicToNatsSubject(actionResult.ResponseAddress ?? responseSubjectBase);

                // send the ActionResponse NetworkMessage to the NATS broker
                await _conn!.PublishAsync(responseSubject, Encoding.UTF8.GetBytes(actionResult.ResponseJson), cancellationToken: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "HandleCommandAsync");
            }
        }

        private static string MqttTopicToNatsSubject(string mqtt) => mqtt.Replace("/", ".").Replace("#", ">").Replace("+", "*").Trim();
    }
}