
namespace Opc.Ua.Cloud.Commander
{
    using Newtonsoft.Json;
    using Opc.Ua;
    using Serilog;
    using System;
    using System.Net.Security;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Web;
    using uPLibrary.Networking.M2Mqtt;
    using uPLibrary.Networking.M2Mqtt.Messages;

    public class MQTTClient
    {
        private ApplicationConfiguration _appConfig = null;
        private MqttClient _mqttClient = null;

        public MQTTClient(ApplicationConfiguration appConfig)
        {
            _appConfig = appConfig;
        }

        public void Connect()
        {
            // create MQTT client
            string brokerName = Environment.GetEnvironmentVariable("BROKERNAME");
            string clientName = Environment.GetEnvironmentVariable("CLIENTNAME");
            string userName = Environment.GetEnvironmentVariable("USERNAME");
            string password = Environment.GetEnvironmentVariable("PASSWORD");
            string topic = Environment.GetEnvironmentVariable("TOPIC");
            _mqttClient = new MqttClient(brokerName, 8883, true, MqttSslProtocols.TLSv1_2, CertificateValidationCallback, null);

            if (Environment.GetEnvironmentVariable("CREATE_SAS_PASSWORD") != null)
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

            // register publish received and disconnect handler callbacks
            _mqttClient.MqttMsgPublishReceived += PublishReceived;
            _mqttClient.ConnectionClosed += ConnectionClosed;

            // subscribe to all our topics
            _mqttClient.Subscribe(new string[] { topic }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });

            // connect to MQTT broker
            byte returnCode = _mqttClient.Connect(clientName, userName, password, false, 5);
            if (returnCode != MqttMsgConnack.CONN_ACCEPTED)
            {
                Log.Logger.Error("Connection to MQTT broker failed with " + returnCode.ToString() + "!");
            }
            else
            {
                Log.Logger.Information("Connected to MQTT broker.");
            }
        }

        private void ConnectionClosed(object sender, EventArgs e)
        {
            Log.Logger.Warning("Disconnected from MQTT broker.");

            // simply reconnect again
            Connect();
        }

        private void PublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            Log.Logger.Information($"Received cloud command with topic: {e.Topic} and payload: {Encoding.UTF8.GetString(e.Message)}");

            string requestTopic = Environment.GetEnvironmentVariable("TOPIC");
            string responseTopic = Environment.GetEnvironmentVariable("RESPONSE_TOPIC");
            string requestID = e.Topic.Substring(e.Topic.IndexOf("?"));

            ResponseModel response = new()
            {
                TimeStamp = DateTime.UtcNow,
            };

            try
            {
                string requestPayload = Encoding.UTF8.GetString(e.Message);

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
                if (e.Topic.StartsWith(requestTopic.TrimEnd('#') + "MethodCall"))
                {
                    new UAClient().ExecuteUACommand(_appConfig, requestPayload);
                    response.Success = true;
                }
                else if (e.Topic.StartsWith(requestTopic.TrimEnd('#') + "Read"))
                {
                    response.Status = new UAClient().ReadUAVariable(_appConfig, requestPayload);
                    response.Success = true;
                }
                else if (e.Topic.StartsWith(requestTopic.TrimEnd('#') + "HistoryRead"))
                {
                    response.Status = new UAClient().ReadUAHistory(_appConfig, requestPayload);
                    response.Success = true;
                }
                else if (e.Topic.StartsWith(requestTopic.TrimEnd('#') + "Write"))
                {
                    new UAClient().WriteUAVariable(_appConfig, requestPayload);
                    response.Success = true;
                }
                else
                {
                    Log.Logger.Error("Unknown command received: " + e.Topic);
                    response.Status = "Unkown command " + e.Topic;
                    response.Success = false;
                }

                // send reponse to MQTT broker
                _mqttClient.Publish(responseTopic + "/200/" + requestID, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);

            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "MQTTBrokerPublishReceived");
                response.Status = ex.Message;
                response.Success = false;

                // send error to MQTT broker
                _mqttClient.Publish(responseTopic + "/500/" + requestID, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);
            }
        }

        private bool CertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // always trust the MQTT broker certificate
            return true;
        }
    }
}
