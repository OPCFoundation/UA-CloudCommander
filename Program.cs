
namespace Opc.Ua.Cloud.Commander
{
    using Opc.Ua;
    using Opc.Ua.Configuration;
    using Serilog;
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public class Program
    {
        public static ConsoleTelemetry Telemetry = new();

        public static async Task Main()
        {
            // create OPC UA client app
            string appName = "UACloudCommander";
            if (Environment.GetEnvironmentVariable("APPNAME") != null)
            {
                appName = Environment.GetEnvironmentVariable("APPNAME");
            }

            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();

            ApplicationInstance app = new ApplicationInstance(Telemetry)
            {
                ApplicationName = appName,
                ApplicationType = ApplicationType.ClientAndServer,
                ConfigSectionName = "UA.Cloud.Commander"
            };

            // update app name in config file
            string fileContent = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "UA.Cloud.Commander.Config.xml"));
            fileContent = fileContent.Replace("UndefinedAppName", appName);
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "UA.Cloud.Commander.Config.xml"), fileContent);

            await app.LoadApplicationConfigurationAsync(false).ConfigureAwait(false);

            bool certOK = await app.CheckApplicationInstanceCertificatesAsync(false, 0).ConfigureAwait(false);
            if (!certOK)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            // create OPC UA cert validator
            app.ApplicationConfiguration.CertificateValidator = new CertificateValidator(Telemetry);
            app.ApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(OPCUAServerCertificateValidationCallback);
            await app.ApplicationConfiguration.CertificateValidator.UpdateAsync(app.ApplicationConfiguration).ConfigureAwait(false);

            string issuerPath = Path.Combine(Directory.GetCurrentDirectory(), "pki", "issuer", "certs");
            if (!Directory.Exists(issuerPath))
            {
                Directory.CreateDirectory(issuerPath);
            }

            // start the server.
            await app.StartAsync(new UAServer()).ConfigureAwait(false);
            Log.Logger.Information("Server started.");

            MQTTClient methodHandlerMQTT = null;
            KafkaClient methodHandlerKafka = null;
            NATSClient methodHandlerNATS = null;
            if (Environment.GetEnvironmentVariable("USE_KAFKA") != null)
            {
                methodHandlerKafka = new KafkaClient(app.ApplicationConfiguration);
                methodHandlerKafka.Connect();
            }
            else if (Environment.GetEnvironmentVariable("USE_NATS") != null)
            {
                methodHandlerNATS = new NATSClient(app.ApplicationConfiguration);
                await methodHandlerNATS.ConnectAsync().ConfigureAwait(false);
            }
            else
            {
                // connect to the MQTT broker
                methodHandlerMQTT = new MQTTClient(app.ApplicationConfiguration);
                await methodHandlerMQTT.ConnectAsync().ConfigureAwait(false);
            }

            Log.Logger.Information("UA Cloud Commander is running.");

            await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
        }

        private static void OPCUAServerCertificateValidationCallback(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            // always trust the OPC UA server certificate
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = true;
            }
        }
    }
}
