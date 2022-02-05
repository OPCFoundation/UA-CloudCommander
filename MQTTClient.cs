
namespace UACommander
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
        private static ApplicationConfiguration _appConfig = null;
        private static MqttClient _mqttClient = null;

        public MQTTClient(ApplicationConfiguration appConfig)
        {
            _appConfig = appConfig;
        }

        public void Subscribe()
        {
            // create MQTT client
            string brokerName = Environment.GetEnvironmentVariable("MQTT_BROKERNAME");
            string clientName = Environment.GetEnvironmentVariable("MQTT_CLIENTNAME");
            string userName = Environment.GetEnvironmentVariable("MQTT_USERNAME");
            string password = Environment.GetEnvironmentVariable("MQTT_PASSWORD");
            string topic = Environment.GetEnvironmentVariable("MQTT_TOPIC");
            _mqttClient = new MqttClient(brokerName, 8883, true, MqttSslProtocols.TLSv1_2, MQTTBrokerCertificateValidationCallback, null);

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

            // register to message received
            _mqttClient.MqttMsgPublishReceived += MQTTBrokerPublishReceived;

            // subscribe to all methods
            _mqttClient.Subscribe(new string[] { topic }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });

            // connect to MQTT broker
            byte returnCode = _mqttClient.Connect(clientName, userName, password);
            if (returnCode != MqttMsgConnack.CONN_ACCEPTED)
            {
                Log.Logger.Error("Connection to MQTT broker failed with " + returnCode.ToString() + "!");
            }
        }

        private static void MQTTBrokerPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            string requestTopic = Environment.GetEnvironmentVariable("MQTT_TOPIC");
            string responseTopic = Environment.GetEnvironmentVariable("MQTT_RESPONSE_TOPIC");
            string requestID = e.Topic.Substring(e.Topic.IndexOf("?"));

            try
            {
                string requestPayload = Encoding.UTF8.GetString(e.Message);
                string responsePayload = string.Empty;

                // route this to the right handler
                if (e.Topic.StartsWith(requestTopic.TrimEnd('#') + "Command"))
                {
                    new UAClient().ExecuteUACommand(_appConfig, requestPayload);
                    responsePayload = "Success";
                }
                else if (e.Topic.StartsWith(requestTopic.TrimEnd('#') + "Read"))
                {
                    responsePayload = new UAClient().ReadUAVariable(_appConfig, requestPayload);
                }
                else if (e.Topic.StartsWith(requestTopic.TrimEnd('#') + "Write"))
                {
                    new UAClient().WriteUAVariable(_appConfig, requestPayload);
                    responsePayload = "Success";
                }
                else
                {
                    Log.Logger.Error("Unknown command received: " + e.Topic);
                }

                // send reponse to MQTT broker
                _mqttClient.Publish(responseTopic + "/200/" + requestID, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(responsePayload)), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);

            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "MQTTBrokerPublishReceived");

                // send to MQTT broker
                _mqttClient.Publish(responseTopic + "/500/" + requestID, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ex.Message)), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);
            }
        }

        private static bool MQTTBrokerCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // always trust the MQTT broker certificate
            return true;
        }
    }
}
