
namespace Opc.Ua.Cloud.Commander
{
    using MQTTnet;
    using MQTTnet.Exceptions;
    using MQTTnet.Packets;
    using MQTTnet.Protocol;
    using Newtonsoft.Json;
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

        public void Connect()
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

                // disconnect if still connected
                if ((_client != null) && _client.IsConnected)
                {
                    _client.DisconnectAsync().GetAwaiter().GetResult();

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
                    HMACSHA256 hmac = new HMACSHA256(Convert.FromBase64String(password));
                    string signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
                    password = "SharedAccessSignature sr=" + HttpUtility.UrlEncode(brokerName + "/devices/" + clientName) + "&sig=" + HttpUtility.UrlEncode(signature) + "&se=" + expiry;
                }

                // create MQTT client
                _client = new MqttClientFactory().CreateMqttClient();
                _client.ApplicationMessageReceivedAsync += msg => HandleMessageAsync(msg);

                MqttClientOptionsBuilder clientOptions = new MqttClientOptionsBuilder()
                        .WithTcpServer(brokerName, brokerPort)
                        .WithClientId(clientName)
                        .WithTlsOptions(new MqttClientTlsOptions { UseTls = useTLS })
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
                        .WithTlsOptions(new MqttClientTlsOptions { UseTls = useTLS })
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
                _client.DisconnectedAsync += disconnectArgs =>
                {
                    Log.Logger.Warning($"Disconnected from MQTT broker: {disconnectArgs.Reason}");

                    // wait a 5 seconds, then simply reconnect again, if needed
                    Task.Delay(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                    if (!_client.IsConnected)
                    {
                        MqttClientConnectResult connectResult = _client.ConnectAsync(clientOptions.Build(), _cancellationTokenSource.Token).GetAwaiter().GetResult();
                        if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                        {
                            string status = GetStatus(connectResult.UserProperties)?.ToString("x4");
                            throw new Exception($"Connection to MQTT broker failed. Status: {connectResult.ResultCode}; status: {status}");
                        }
                    }

                    return Task.CompletedTask;
                };

                try
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = new CancellationTokenSource();

                    MqttClientConnectResult connectResult = _client.ConnectAsync(clientOptions.Build(), _cancellationTokenSource.Token).GetAwaiter().GetResult();
                    if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                    {
                        string status = GetStatus(connectResult.UserProperties)?.ToString("x4");
                        throw new Exception($"Connection to MQTT broker failed. Status: {connectResult.ResultCode}; status: {status}");
                    }

                    if (!string.IsNullOrEmpty(topic))
                    {
                        MqttClientSubscribeResult subscribeResult = _client.SubscribeAsync(
                        new MqttTopicFilter
                        {
                            Topic = topic,
                            QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce
                        }).GetAwaiter().GetResult();

                        // make sure subscriptions were successful
                        if (subscribeResult.Items.Count != 1 || subscribeResult.Items.ElementAt(0).ResultCode != MqttClientSubscribeResultCode.GrantedQoS0)
                        {
                            throw new ApplicationException("Failed to subscribe");
                        }
                    }

                    Log.Logger.Information("Connected to MQTT broker.");
                }
                catch (MqttCommunicationException ex)
                {
                    Log.Logger.Error($"Failed to connect with reason {ex.HResult} and message: {ex.Message}");
                    if ((ex.Data != null) && (ex.Data.Count > 0))
                    {
                        foreach (var prop in ex.Data)
                        {
                            Log.Logger.Error($"{prop.ToString()}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Failed to connect to MQTT broker: " + ex.Message);
            }
        }

        private MqttApplicationMessage BuildResponse(string status, string id, byte[] payload)
        {
            string responseTopic = Environment.GetEnvironmentVariable("RESPONSE_TOPIC");

            return new MqttApplicationMessageBuilder()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopic($"{responseTopic}/{status}/{id}")
                .WithPayload(payload)
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

            return int.Parse(status.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        // handles all incoming messages
        private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            Log.Logger.Information($"Received cloud command with topic: {args.ApplicationMessage.Topic} and payload: {args.ApplicationMessage.ConvertPayloadToString()}");

            string requestTopic = Environment.GetEnvironmentVariable("TOPIC");
            string requestID = args.ApplicationMessage.Topic.Substring(args.ApplicationMessage.Topic.IndexOf("?"));

            ResponseModel response = new()
            {
                TimeStamp = DateTime.UtcNow,
            };

            try
            {
                string requestPayload = args.ApplicationMessage.ConvertPayloadToString();

                // parse the message
                RequestModel request = JsonConvert.DeserializeObject<RequestModel>(requestPayload);

                // discard messages that are older than 15 seconds
                if (request.TimeStamp < DateTime.UtcNow.AddSeconds(-15))
                {
                    Log.Logger.Information($"Discarding old message with timestamp {request.TimeStamp}");
                    return;
                }

                response.CorrelationId = request.CorrelationId;

                // route this to the right handler
                if (request.Command == "MethodCall")
                {
                    response.Status = new UAClient().ExecuteUACommand(_uAApplication, requestPayload);
                    response.Success = true;
                }
                else if (request.Command == "Read")
                {
                    response.Status = new UAClient().ReadUAVariable(_uAApplication, requestPayload);
                    response.Success = true;
                }
                else if (request.Command == "HistoricalRead")
                {
                    response.Status = new UAClient().ReadUAHistory(_uAApplication, requestPayload);
                    response.Success = true;
                }
                else if (request.Command == "Write")
                {
                    new UAClient().WriteUAVariable(_uAApplication, requestPayload);
                    response.Success = true;
                }
                else
                {
                    Log.Logger.Error("Unknown command received: " + request.Command);
                    response.Status = "Unkown command " + request.Command;
                    response.Success = false;
                }

                // send reponse to MQTT broker
                await _client.PublishAsync(BuildResponse("200", requestID, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response))), _cancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "HandleMessageAsync");
                response.Status = ex.Message;
                response.Success = false;

                // send error to MQTT broker
                await _client.PublishAsync(BuildResponse("500", requestID, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response))), _cancellationTokenSource.Token).ConfigureAwait(false);
            }
        }
    }
}
