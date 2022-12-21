
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
        private static AzureFileStorage _storage = new AzureFileStorage();

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
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "UA.Cloud.Commander"
            };

            // update app name in config file
            string fileContent = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "UA.Cloud.Commander.Config.xml"));
            fileContent = fileContent.Replace("UndefinedAppName", appName);
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "UA.Cloud.Commander.Config.xml"), fileContent);

            LoadCertsFromCloud(appName);

            await app.LoadApplicationConfiguration(false).ConfigureAwait(false);

            bool certOK = await app.CheckApplicationInstanceCertificate(false, 0).ConfigureAwait(false);
            if (!certOK)
            {
                throw new Exception("Application instance certificate invalid!");
            }
            else
            {
                StoreCertsInCloud();
            }

            // create OPC UA cert validator
            app.ApplicationConfiguration.CertificateValidator = new CertificateValidator();
            app.ApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(OPCUAServerCertificateValidationCallback);

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

        private static void StoreCertsInCloud()
        {
            // store app certs
            foreach (string filePath in Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "pki", "own", "certs"), "*.der"))
            {
                _storage.StoreFileAsync(filePath, File.ReadAllBytesAsync(filePath).GetAwaiter().GetResult()).GetAwaiter().GetResult();
            }

            // store private keys
            foreach (string filePath in Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "pki", "own", "private"), "*.pfx"))
            {
                _storage.StoreFileAsync(filePath, File.ReadAllBytesAsync(filePath).GetAwaiter().GetResult()).GetAwaiter().GetResult();
            }

            // store trusted certs
            foreach (string filePath in Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "pki", "trusted", "certs"), "*.der"))
            {
                _storage.StoreFileAsync(filePath, File.ReadAllBytesAsync(filePath).GetAwaiter().GetResult()).GetAwaiter().GetResult();
            }
        }

        private static void LoadCertsFromCloud(string appName)
        {
            try
            {
                // load app cert from storage
                string certFilePath = _storage.FindFileAsync(Path.Combine(Directory.GetCurrentDirectory(), "pki", "own", "certs"), appName).GetAwaiter().GetResult();
                byte[] certFile = _storage.LoadFileAsync(certFilePath).GetAwaiter().GetResult();
                if (certFile == null)
                {
                    Console.WriteLine("Could not load cert file, creating a new one. This means the new cert needs to be trusted by all OPC UA servers we connect to!");
                }
                else
                {
                    if (!Path.IsPathRooted(certFilePath))
                    {
                        certFilePath = Path.DirectorySeparatorChar.ToString() + certFilePath;
                    }

                    if (!Directory.Exists(Path.GetDirectoryName(certFilePath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(certFilePath));
                    }

                    File.WriteAllBytes(certFilePath, certFile);
                }

                // load app private key from storage
                string keyFilePath = _storage.FindFileAsync(Path.Combine(Directory.GetCurrentDirectory(), "pki", "own", "private"), appName).GetAwaiter().GetResult();
                byte[] keyFile = _storage.LoadFileAsync(keyFilePath).GetAwaiter().GetResult();
                if (keyFile == null)
                {
                    Console.WriteLine("Could not load key file, creating a new one. This means the new cert generated from the key needs to be trusted by all OPC UA servers we connect to!");
                }
                else
                {
                    if (!Path.IsPathRooted(keyFilePath))
                    {
                        keyFilePath = Path.DirectorySeparatorChar.ToString() + keyFilePath;
                    }

                    if (!Directory.Exists(Path.GetDirectoryName(keyFilePath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(keyFilePath));
                    }

                    File.WriteAllBytes(keyFilePath, keyFile);
                }

                // load trusted certs
                string[] trustedcertFilePath = _storage.FindFilesAsync(Path.Combine(Directory.GetCurrentDirectory(), "pki", "trusted", "certs")).GetAwaiter().GetResult();
                if (trustedcertFilePath != null)
                {
                    foreach (string filePath in trustedcertFilePath)
                    {
                        byte[] trustedcertFile = _storage.LoadFileAsync(filePath).GetAwaiter().GetResult();
                        if (trustedcertFile == null)
                        {
                            Console.WriteLine("Could not load trusted cert file " + filePath);
                        }
                        else
                        {
                            string localFilePath = filePath;

                            if (!Path.IsPathRooted(localFilePath))
                            {
                                localFilePath = Path.DirectorySeparatorChar.ToString() + localFilePath;
                            }

                            if (!Directory.Exists(Path.GetDirectoryName(localFilePath)))
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(localFilePath));
                            }

                            File.WriteAllBytes(localFilePath, trustedcertFile);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Cloud not load cert or private key files, creating a new ones. This means the new cert needs to be trusted by all OPC UA servers we connect to!: " + ex.Message);
            }
        }
    }
}
