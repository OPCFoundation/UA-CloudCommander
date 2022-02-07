
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
        public static async Task Main(string[] args)
        {
            // setup logging
            string pathToLogFile = Directory.GetCurrentDirectory();
            if (Environment.GetEnvironmentVariable("LOG_FILE_PATH") != null)
            {
                pathToLogFile = Environment.GetEnvironmentVariable("LOG_FILE_PATH");
            }
            InitLogging(pathToLogFile);

            // create OPC UA client app
            string appName = "UACommander";
            if (args.Length > 0)
            {
                appName = args[0];
            }
            ApplicationInstance app = new ApplicationInstance
            {
                ApplicationName = appName,
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "UA.Commander"
            };
                        
            // redirect cert store location, if required and update cert issuer name
            if (Environment.GetEnvironmentVariable("CERT_STORE_PATH") != null)
            {
                string certStorePath = Environment.GetEnvironmentVariable("CERT_STORE_PATH");
                string fileContent = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "UA.Commander.Config.xml"));
                fileContent = fileContent.Replace(">%LocalApplicationData%/UACommander/pki/trusted<", ">" + certStorePath + "<");
                fileContent = fileContent.Replace("CN=UACommander", "CN=" + app.ApplicationName);
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "UA.Commander.Config.xml"), fileContent);
            }

            await app.LoadApplicationConfiguration(false).ConfigureAwait(false);
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
