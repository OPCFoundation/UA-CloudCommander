
namespace Opc.Ua.Cloud.Commander
{
    using Opc.Ua;
    using Opc.Ua.Configuration;
    using Serilog;
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public class Program
    {
        private static int _traceMasks = 1; // default to errors only

        public static async Task Main()
        {
            InitLogging(Directory.GetCurrentDirectory());

            // create OPC UA client app
            string appName = "UACloudCommander";
            if (Environment.GetEnvironmentVariable("APPNAME") != null)
            {
                appName = Environment.GetEnvironmentVariable("APPNAME");
            }

            ApplicationInstance app = new ApplicationInstance
            {
                ApplicationName = appName,
                ApplicationType = ApplicationType.ClientAndServer,
                ConfigSectionName = "UA.Cloud.Commander"
            };

            // update app name in config file
            string fileContent = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "UA.Cloud.Commander.Config.xml"));
            fileContent = fileContent.Replace("UndefinedAppName", appName);
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "UA.Cloud.Commander.Config.xml"), fileContent);

            await app.LoadApplicationConfiguration(false).ConfigureAwait(false);

            // hook up OPC UA stack traces
            _traceMasks = app.ApplicationConfiguration.TraceConfiguration.TraceMasks;
            Utils.Tracing.TraceEventHandler += new EventHandler<TraceEventArgs>(OpcStackLoggingHandler);

            bool certOK = await app.CheckApplicationInstanceCertificates(false, 0).ConfigureAwait(false);
            if (!certOK)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            // create OPC UA cert validator
            app.ApplicationConfiguration.CertificateValidator = new CertificateValidator();
            app.ApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(OPCUAServerCertificateValidationCallback);
            app.ApplicationConfiguration.CertificateValidator.Update(app.ApplicationConfiguration).GetAwaiter().GetResult();

            string issuerPath = Path.Combine(Directory.GetCurrentDirectory(), "pki", "issuer", "certs");
            if (!Directory.Exists(issuerPath))
            {
                Directory.CreateDirectory(issuerPath);
            }

            // start the server.
            app.Start(new UAServer()).GetAwaiter().GetResult();
            Log.Logger.Information("Server started.");

            MQTTClient methodHandlerMQTT = null;
            KafkaClient methodHandlerKafka = null;
            if (Environment.GetEnvironmentVariable("USE_KAFKA") != null)
            {
                methodHandlerKafka = new KafkaClient(app.ApplicationConfiguration);
                methodHandlerKafka.Connect();
            }
            else
            {
                // connect to the MQTT broker
                methodHandlerMQTT = new MQTTClient(app.ApplicationConfiguration);
                methodHandlerMQTT.Connect();
            }

            Log.Logger.Information("UA Cloud Commander is running.");

            await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
        }

        private static void OpcStackLoggingHandler(object sender, TraceEventArgs e)
        {
            if ((e.TraceMask & _traceMasks) != 0)
            {
                if (e.Arguments != null)
                {
                    Console.WriteLine("OPC UA Stack: " + string.Format(CultureInfo.InvariantCulture, e.Format, e.Arguments).Trim());
                }
                else
                {
                    Console.WriteLine("OPC UA Stack: " + e.Format.Trim());
                }
            }
        }

        private static void OPCUAServerCertificateValidationCallback(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            // always trust the OPC UA server certificate
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = true;
            }
        }

        private static void InitLogging(string pathToLogFile)
        {
            LoggerConfiguration loggerConfiguration = new LoggerConfiguration();

#if DEBUG
            loggerConfiguration.MinimumLevel.Debug();
#else
            loggerConfiguration.MinimumLevel.Information();
#endif
            if (!Directory.Exists(pathToLogFile))
            {
                Directory.CreateDirectory(pathToLogFile);
            }

            // set logging sinks
            loggerConfiguration.WriteTo.Console();
            loggerConfiguration.WriteTo.File(Path.Combine(pathToLogFile, "uacloudcommander.logfile.txt"), fileSizeLimitBytes: 1024 * 1024, rollOnFileSizeLimit: true, retainedFileCountLimit: 10);

            Log.Logger = loggerConfiguration.CreateLogger();
            Log.Logger.Information($"Log file is: {Path.Combine(pathToLogFile, "uacloudcommander.logfile.txt")}");
        }
    }
}
