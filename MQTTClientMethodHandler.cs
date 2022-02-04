
namespace UACommander
{
    using Opc.Ua;
    using Serilog;
    using System;
    using System.Net.Security;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using uPLibrary.Networking.M2Mqtt;
    using uPLibrary.Networking.M2Mqtt.Messages;

    public class MQTTClientMethodHandler : IMethodHandler
    {
        private ApplicationConfiguration _appConfig;
        
        public MQTTClientMethodHandler(ApplicationConfiguration appConfig)
        {
            _appConfig = appConfig;
        }

        public void HandleCommand(string payload)
        {
            new UAClient().ExecuteUACommand(_appConfig, payload);
        }

        public void HandleWrite(string payload)
        {
            new UAClient().WriteUAVariable(_appConfig, payload);
        }

        public Task RegisterMethodsAsync(string connectionString, CancellationToken cancellationToken)
        {
            // create MQTT client
            string clientName = Environment.GetEnvironmentVariable("MQTT_USERNAME");
            string sharedKey = Environment.GetEnvironmentVariable("MQTT_PASSWORD");
            string userName = connectionString + "/" + clientName + "/?api-version=2018-06-30";
            MqttClient mqttClient = new MqttClient(connectionString, 8883, true, MqttSslProtocols.TLSv1_2, MQTTBrokerCertificateValidationCallback, null);

            // create SAS token
            TimeSpan sinceEpoch = DateTime.UtcNow - new DateTime(1970, 1, 1);
            int week = 60 * 60 * 24 * 7;
            string expiry = Convert.ToString((int)sinceEpoch.TotalSeconds + week);
            string stringToSign = HttpUtility.UrlEncode(connectionString + "/devices/" + clientName) + "\n" + expiry;
            HMACSHA256 hmac = new HMACSHA256(Convert.FromBase64String(sharedKey));
            string signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
            string password = "SharedAccessSignature sr=" + HttpUtility.UrlEncode(connectionString + "/devices/" + clientName) + "&sig=" + HttpUtility.UrlEncode(signature) + "&se=" + expiry;
            
            // register to message received 
            mqttClient.MqttMsgPublishReceived += MQTTBrokerPublishReceived;

            // register ourselves for notifications for a "Command" and a "Write" topic
            mqttClient.Subscribe(new string[] { "Command", "Write" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });

            // connect to MQTT broker
            byte returnCode = mqttClient.Connect(clientName, userName, password);
            if (returnCode != MqttMsgConnack.CONN_ACCEPTED)
            {
                Log.Logger.Error("Connection to MQTT broker failed with " + returnCode.ToString() + "!");
            }

            return Task.CompletedTask;
        }

        private static void MQTTBrokerPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            string message = Encoding.UTF8.GetString(e.Message);

            // TODO: Route this to the right handler
        }

        private static bool MQTTBrokerCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // always trust the MQTT broker certificate
            return true;
        }
    }
}
