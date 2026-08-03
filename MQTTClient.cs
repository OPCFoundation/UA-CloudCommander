namespace Opc.Ua.Cloud.Commander
{
    using MQTTnet;
    using MQTTnet.Exceptions;
    using MQTTnet.Packets;
    using MQTTnet.Protocol;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;

    public class MQTTClient
    {
        private IMqttClient _client = null;

        private readonly ApplicationConfiguration _uAApplication;

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public MQTTClient(ApplicationConfiguration uAApplication)
        {
            _uAApplication = uAApplication;
        }

        public class MqttClientCertificatesProvider : IMqttClientCertificatesProvider
        {
            private readonly ApplicationConfiguration _uAApplication;

            public MqttClientCertificatesProvider(ApplicationConfiguration uAApplication)
            {
                _uAApplication = uAApplication;
            }

            X509CertificateCollection IMqttClientCertificatesProvider.GetCertificates()
            {
                X509Certificate2 appCert = _uAApplication.SecurityConfiguration.ApplicationCertificate.Certificate;
                if (appCert == null)
                {
                    throw new Exception($"Cannot access OPC UA application certificate!");
                }

                return new X509CertificateCollection() { appCert };
            }
        }

        public async Task ConnectAsync()
        {
            try
            {
                string brokerName = Environment.GetEnvironmentVariable("BROKERNAME");
                int brokerPort = int.Parse(Environment.GetEnvironmentVariable("BROKERPORT"));
                string clientName = Environment.GetEnvironmentVariable("CLIENTNAME");
                string userName = Environment.GetEnvironmentVariable("USERNAME");
                string password = Environment.GetEnvironmentVariable("PASSWORD");
                string topic = Environment.GetEnvironmentVariable("TOPIC");
                bool createBrokerSASToken = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CREATE_SAS_PASSWORD"));
                bool useTLS = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USE_TLS"));
                bool useUACertAuth = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USE_UA_CERT_AUTH"));
                // Accept broker certificates that fail chain validation (self-signed or signed by a
                // private CA). Opt-in only, so certificate validation stays strict by default.
                bool allowUntrustedBrokerCert = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALLOW_UNTRUSTED_BROKER_CERT"));

                // disconnect if still connected
                if (_client != null)
                {
                    if (_client.IsConnected)
                    {
                        await _client.DisconnectAsync().ConfigureAwait(false);
                    }

                    _client.Dispose();
                    _client = null;
                    _cancellationTokenSource.Cancel();
                }

                if (string.IsNullOrEmpty(brokerName))
                {
                    // no broker URL configured = nothing to connect to!
                    Log.Logger.Error("Broker URL not configured. Cannot connect to broker!");
                    return;
                }

                // create MQTT password
                if (createBrokerSASToken)
                {
                    // create SAS token as password
                    TimeSpan sinceEpoch = DateTime.UtcNow - new DateTime(1970, 1, 1);
                    int week = 60 * 60 * 24 * 7;
                    string expiry = Convert.ToString((int)sinceEpoch.TotalSeconds + week);
                    string stringToSign = HttpUtility.UrlEncode(brokerName + "/devices/" + clientName) + "\n" + expiry;
                    using HMACSHA256 hmac = new HMACSHA256(Convert.FromBase64String(password));
                    string signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
                    password = "SharedAccessSignature sr=" + HttpUtility.UrlEncode(brokerName + "/devices/" + clientName) + "&sig=" + HttpUtility.UrlEncode(signature) + "&se=" + expiry;
                }

                // create MQTT client
                _client = new MqttClientFactory().CreateMqttClient();
                _client.ApplicationMessageReceivedAsync += msg => HandleMessageAsync(msg);

                MqttClientOptionsBuilder clientOptions = new MqttClientOptionsBuilder()
                        .WithTcpServer(brokerName, brokerPort)
                        .WithClientId(clientName)
                        .WithTlsOptions(BuildTlsOptions(useTLS, allowUntrustedBrokerCert))
                        .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
                        .WithTimeout(TimeSpan.FromSeconds(10))
                        .WithKeepAlivePeriod(TimeSpan.FromSeconds(100))
                        .WithCleanSession(true) // clear existing subscriptions
                        .WithCredentials(userName, password);

                if (brokerPort == 443)
                {
                    clientOptions = new MqttClientOptionsBuilder()
                        .WithWebSocketServer( o => o.WithUri(brokerName))
                        .WithClientId(clientName)
                        .WithTlsOptions(BuildTlsOptions(useTLS, allowUntrustedBrokerCert))
                        .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
                        .WithTimeout(TimeSpan.FromSeconds(10))
                        .WithKeepAlivePeriod(TimeSpan.FromSeconds(100))
                        .WithCleanSession(true) // clear existing subscriptions
                        .WithCredentials(userName, password);
                }

                if (useUACertAuth)
                {
                    clientOptions = new MqttClientOptionsBuilder()
                        .WithTcpServer(brokerName)
                        .WithClientId(clientName)
                        .WithTlsOptions(new MqttClientTlsOptions
                        {
                            UseTls = true,
                            AllowUntrustedCertificates = true,
                            IgnoreCertificateChainErrors = true,
                            ClientCertificatesProvider = new MqttClientCertificatesProvider(_uAApplication)
                        })
                        .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
                        .WithTimeout(TimeSpan.FromSeconds(10))
                        .WithKeepAlivePeriod(TimeSpan.FromSeconds(100))
                        .WithCleanSession(true) // clear existing subscriptions
                        .WithCredentials(clientName, string.Empty);
                }

                // setup disconnection handling
                _client.DisconnectedAsync += async disconnectArgs =>
                {
                    Log.Logger.Warning($"Disconnected from MQTT broker: {disconnectArgs.Reason}");

                    // wait a 5 seconds, then simply reconnect again, if needed
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                    if (!_client.IsConnected)
                    {
                        // Never let an exception escape this handler. MQTTnet invokes it on its
                        // own dispatch loop, so a throw here is unobserved and - more importantly -
                        // no further DisconnectedAsync event is raised for this attempt, which
                        // permanently ends the reconnect cycle. That turns a broker that is merely
                        // slow to start (e.g. both come up together after a host reboot) into a
                        // Commander that stays offline until it is manually restarted.
                        try
                        {
                            MqttClientConnectResult connectResult = await _client.ConnectAsync(clientOptions.Build(), _cancellationTokenSource.Token).ConfigureAwait(false);
                            if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                            {
                                string status = GetStatus(connectResult.UserProperties)?.ToString("x4");
                                Log.Logger.Error($"Reconnect to MQTT broker failed. Status: {connectResult.ResultCode}; status: {status}. Retrying.");
                            }
                            else
                            {
                                // Subscriptions do not survive the reconnect: the session is opened
                                // with CleanSession, so the broker discards them on disconnect.
                                if (!string.IsNullOrEmpty(topic))
                                {
                                    await _client.SubscribeAsync(
                                        new MqttTopicFilter
                                        {
                                            Topic = topic,
                                            QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce
                                        }).ConfigureAwait(false);
                                }

                                Log.Logger.Information("Reconnected to MQTT broker.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Logger.Error($"Reconnect to MQTT broker failed: {ex.Message}. Retrying.");
                        }
                    }
                };

                // A previous Disconnect() cancels the token source, so give this connection
                // attempt a fresh one - otherwise ConnectAsync would fail immediately on an
                // already-cancelled token.
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();

                // Retry the initial connect. There is no init container gating startup on the
                // broker, so on a host reboot the Commander regularly starts before Mosquitto is
                // listening. A single failed attempt would leave the process running but offline:
                // MQTTnet only raises DisconnectedAsync for a connection that was previously
                // established, so nothing would drive the reconnect handler.
                int attempt = 0;
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    attempt++;
                    try
                    {
                        MqttClientConnectResult connectResult = await _client.ConnectAsync(clientOptions.Build(), _cancellationTokenSource.Token).ConfigureAwait(false);
                        if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                        {
                            string status = GetStatus(connectResult.UserProperties)?.ToString("x4");
                            throw new Exception($"Connection to MQTT broker failed. Status: {connectResult.ResultCode}; status: {status}");
                        }

                        if (!string.IsNullOrEmpty(topic))
                        {
                            MqttClientSubscribeResult subscribeResult = await _client.SubscribeAsync(
                            new MqttTopicFilter
                            {
                                Topic = topic,
                                QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce
                            }).ConfigureAwait(false);

                            // make sure subscriptions were successful
                            if (subscribeResult.Items.Count != 1 || subscribeResult.Items.ElementAt(0).ResultCode != MqttClientSubscribeResultCode.GrantedQoS0)
                            {
                                throw new ApplicationException("Failed to subscribe");
                            }
                        }

                        Log.Logger.Information("Connected to MQTT broker.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (ex is MqttCommunicationException mqttEx && mqttEx.Data != null && mqttEx.Data.Count > 0)
                        {
                            foreach (var prop in mqttEx.Data)
                            {
                                Log.Logger.Error($"{prop.ToString()}");
                            }
                        }

                        // Back off from 5s up to 60s so a broker that stays down does not fill the log.
                        int delaySeconds = Math.Min(5 * attempt, 60);
                        Log.Logger.Error($"Failed to connect to MQTT broker (attempt {attempt}): {ex.Message}. Retrying in {delaySeconds}s.");

                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _cancellationTokenSource.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Failed to connect to MQTT broker: " + ex.Message);
            }
        }

        // Builds the TLS options for a broker connection. Note that setting AllowUntrustedCertificates and
        // IgnoreCertificateChainErrors alone is not enough in MQTTnet: its default validation callback still
        // rejects certificates (e.g. on hostname mismatch or an untrusted root), which surfaces as
        // "The remote certificate was rejected by the provided RemoteCertificateValidationCallback".
        // We therefore install an explicit validation handler when the user opted in to untrusted certificates.
        private static MqttClientTlsOptions BuildTlsOptions(bool useTls, bool allowUntrusted)
        {
            MqttClientTlsOptions tlsOptions = new MqttClientTlsOptions
            {
                UseTls = useTls,
                AllowUntrustedCertificates = allowUntrusted,
                IgnoreCertificateChainErrors = allowUntrusted
            };

            if (allowUntrusted)
            {
                tlsOptions.CertificateValidationHandler = _ => true;
            }

            return tlsOptions;
        }

        private static MqttApplicationMessage BuildResponse(string responseTopic, string payload)
        {
            return new MqttApplicationMessageBuilder()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopic(responseTopic)
                .WithPayload(Encoding.UTF8.GetBytes(payload))
                .Build();
        }

        // parses status from packet properties
        private int? GetStatus(List<MqttUserProperty> properties)
        {
            if (properties == null)
            {
                return null;
            }

            MqttUserProperty status = properties.FirstOrDefault(up => up.Name == "status");
            if (status == null)
            {
                return null;
            }

            return int.Parse(status.ReadValueAsString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        // handles all incoming messages
        private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            Log.Logger.Information($"Received cloud command with topic: {args.ApplicationMessage.Topic} and payload: {args.ApplicationMessage.ConvertPayloadToString()}");

            try
            {
                string requestPayload = args.ApplicationMessage.ConvertPayloadToString();

                // execute the spec-compliant OPC UA PubSub ActionRequest and build the ActionResponse
                PubSubActionResult result = await PubSubActionHandler.ProcessRequestAsync(_uAApplication, requestPayload).ConfigureAwait(false);
                if (!result.ShouldRespond)
                {
                    return;
                }

                // the Requestor's ResponseAddress takes precedence over the configured response topic
                string responseTopic = result.ResponseAddress ?? Environment.GetEnvironmentVariable("RESPONSE_TOPIC");

                // send the ActionResponse NetworkMessage to the MQTT broker
                await _client.PublishAsync(BuildResponse(responseTopic, result.ResponseJson), _cancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "HandleMessageAsync");
            }
        }
    }
}
