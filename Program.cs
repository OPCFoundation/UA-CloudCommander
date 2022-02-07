
namespace UACommander
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
        public static async Task Main()
        {
            // setup logging
            string pathToLogFile = Directory.GetCurrentDirectory();
            if (Environment.GetEnvironmentVariable("LOG_FILE_PATH") != null)
            {
                pathToLogFile = Environment.GetEnvironmentVariable("LOG_FILE_PATH");
            }
            InitLogging(pathToLogFile);

            // create OPC UA client app
            ApplicationInstance app = new ApplicationInstance
            {
                ApplicationName = "UACommander",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "UA.Commander"
            };

            await app.LoadApplicationConfiguration(false).ConfigureAwait(false);

            // redirect cert store location, if required
            string pathToCertFile = app.ApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StorePath;
            if (Environment.GetEnvironmentVariable("CERT_STORE_PATH") != null)
            {
                pathToLogFile = Environment.GetEnvironmentVariable("CERT_STORE_PATH");
            }
            app.ApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StorePath = pathToLogFile;

            await app.CheckApplicationInstanceCertificate(false, 0).ConfigureAwait(false);

            // create OPC UA cert validator
            app.ApplicationConfiguration.CertificateValidator = new CertificateValidator();
            app.ApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(OPCUAServerCertificateValidationCallback);

            // connect to the MQTT broker
            MQTTClient methodHandler = new MQTTClient(app.ApplicationConfiguration);
            methodHandler.Connect();

            Log.Logger.Information("UA Commander is running.");
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
            loggerConfiguration.WriteTo.File(Path.Combine(pathToLogFile, "ua.commander.logfile.txt"), fileSizeLimitBytes: 1024 * 1024, rollOnFileSizeLimit: true, retainedFileCountLimit: 10);
            
            Log.Logger = loggerConfiguration.CreateLogger();
            Log.Logger.Information($"Log file is: {Path.Combine(pathToLogFile, "ua.commander.logfile.txt")}");
        }
    }
}
