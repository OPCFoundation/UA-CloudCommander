
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

            app.LoadApplicationConfiguration(false).GetAwaiter().GetResult();
            app.CheckApplicationInstanceCertificate(false, 0).GetAwaiter().GetResult();

            // create OPC UA cert validator
            app.ApplicationConfiguration.CertificateValidator = new CertificateValidator();
            app.ApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(OPCUAServerCertificateValidationCallback);

            // create our method handler
            string methodHandlerString = "IoTHubMethodHandler";
            if (Environment.GetEnvironmentVariable("METHOD_HANDLER") != null)
            {
                methodHandlerString = Environment.GetEnvironmentVariable("METHOD_HANDLER");
            }

            IMethodHandler methodHandler = null;
            switch (methodHandlerString)
            {
                case "IoTHubMethodHandler": methodHandler = new IoTHubMethodHandler(app.ApplicationConfiguration); break;
                case "MQTTClientMethodHandler": methodHandler = new MQTTClientMethodHandler(app.ApplicationConfiguration); break;
            }

            // register our methods
            await methodHandler.RegisterMethodsAsync(Environment.GetEnvironmentVariable("CONNECTION_STRING")).ConfigureAwait(false);

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
            
            // set logging sinks
            loggerConfiguration.WriteTo.Console();
            loggerConfiguration.WriteTo.File(Path.Combine(pathToLogFile, "ua.commander.logfile.txt"), fileSizeLimitBytes: 1024 * 1024, rollOnFileSizeLimit: true, retainedFileCountLimit: 10);
            
            Log.Logger = loggerConfiguration.CreateLogger();
            Log.Logger.Information($"Log file is: {Path.Combine(pathToLogFile, "ua.commander.logfile.txt")}");
        }
    }
}
