
namespace UACommander
{
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.Azure.Devices.Provisioning.Client;
    using Microsoft.Azure.Devices.Provisioning.Client.Transport;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TransportType = Microsoft.Azure.Devices.Client.TransportType;

    public class IoTHubMethodHandler : IMethodHandler, IDisposable
    {
        private DeviceClient _deviceClient;
        private ModuleClient _moduleClient;

        private ApplicationConfiguration _appConfig;

        public IoTHubMethodHandler(ApplicationConfiguration appConfig)
        {
            _appConfig = appConfig;
        }

        public string ProductInfo
        {
            get { return "UACommander"; }
        }

        public void Dispose()
        {
            try
            {
                if (_deviceClient != null)
                {
                    _deviceClient.CloseAsync().GetAwaiter().GetResult();
                    _deviceClient.Dispose();
                }

                if (_moduleClient != null)
                {
                    _moduleClient.CloseAsync().GetAwaiter().GetResult();
                    _moduleClient.Dispose();
                }
            }
            catch (Exception e)
            {
                Log.Logger.Error(e, "Failure while shutting down IoT Hub communication.");
            }
        }

        private void ConnectionStatusChange(ConnectionStatus status, ConnectionStatusChangeReason reason)
        {
            Log.Logger.Information("Hub Connection status changed to '{status}', reason '{reason}'", status, reason);
        }

        public async Task RegisterMethodsAsync(string connectionString, CancellationToken cancellationToken = default)
        {
            try
            {
                List<ITransportSettings> transportSettingsList = new List<ITransportSettings>();

                if (Environment.GetEnvironmentVariable("RUN_AS_DEVICE") == null)
                {
                    Log.Logger.Information("Creating IoT Edge module client from environment using MQTT for communication.");

                    var transportSettings = new MqttTransportSettings(TransportType.Mqtt_WebSocket_Only);
                    if (Environment.GetEnvironmentVariable("WEB_PROXY_CONFIG") != null)
                    {
                        transportSettings.Proxy = new WebProxy(Environment.GetEnvironmentVariable("WEB_PROXY_URL"));
                    };
                    transportSettingsList.Add(transportSettings);

                    _moduleClient = await ModuleClient.CreateFromEnvironmentAsync(transportSettingsList.ToArray()).ConfigureAwait(false);

                    _moduleClient.SetRetryPolicy(new ExponentialBackoff(int.MaxValue, TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(1024), TimeSpan.FromMilliseconds(3)));

                    await RegisterHandlersAsync(_moduleClient, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    Log.Logger.Information("Creating IoT Hub device client from connection string using MQTT for communication.");

                    if (string.IsNullOrEmpty(connectionString))
                    {
                        const string errorMessage = "Please pass in the device connection string. Cannot connect to IoT Hub!";
                        Log.Logger.Error("{errorMessage}", errorMessage);
                        throw new ArgumentException(errorMessage);
                    }

                    // check for DPS info
                    if (connectionString.Contains(','))
                    {
                        _deviceClient = CreatDeviceClientFromDPS(connectionString);
                    }
                    else
                    {
                        if (connectionString.Contains(";GatewayHostName="))
                        {
                            // transparent gateway mode
                            Log.Logger.Information("Configuring for transparent gateway mode...");
                            var transportSettings = new MqttTransportSettings(TransportType.Mqtt_WebSocket_Only)
                            {
                                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true,
                            };
                            if (Environment.GetEnvironmentVariable("WEB_PROXY_CONFIG") != null)
                            {
                                transportSettings.Proxy = new WebProxy(Environment.GetEnvironmentVariable("WEB_PROXY_URL"));
                            };
                            transportSettingsList.Add(transportSettings);

                            _deviceClient = DeviceClient.CreateFromConnectionString(connectionString, transportSettingsList.ToArray());
                        }
                        else
                        {
                            var transportSettingsMqttTcpOnly = new MqttTransportSettings(TransportType.Mqtt_WebSocket_Only);
                            if (Environment.GetEnvironmentVariable("WEB_PROXY_CONFIG") != null)
                            {
                                transportSettingsMqttTcpOnly.Proxy = new WebProxy(Environment.GetEnvironmentVariable("WEB_PROXY_URL"));
                            };
                            transportSettingsList.Add(transportSettingsMqttTcpOnly);

                            _deviceClient = DeviceClient.CreateFromConnectionString(connectionString, transportSettingsList.ToArray());
                        }
                    }

                    _deviceClient.SetRetryPolicy(new ExponentialBackoff(int.MaxValue, TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(1024), TimeSpan.FromMilliseconds(3)));

                    await RegisterHandlersAsync(_deviceClient, cancellationToken).ConfigureAwait(false);
                }

                Log.Logger.Information("IoT Hub communication opened successfully.");
            }
            catch (AggregateException ex)
            {
                foreach (var innerException in ex.Flatten().InnerExceptions)
                {
                    Log.Logger.Error(innerException, innerException.Message);
                }
                Log.Logger.Error("Failed to register method handlers.");
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Failed to register method handlers: {message}", ex.Message);
            }
        }

        private DeviceClient CreatDeviceClientFromDPS(string connectionString)
        {
            Log.Logger.Information("Creating device client using DPS...");

            if (string.IsNullOrEmpty(connectionString) || (connectionString.Split(',').Length < 5))
            {
                const string errorMessage = "Expected device credentials deviceId, scopeId, primary key, " +
                    "secondary key and the FQDN of the Edge gateway, comma seperated.";
                Log.Logger.Error("{errorMessage}", errorMessage);
                throw new ArgumentException(errorMessage);
            }

            string[] tokens = connectionString.Split(',');
            string deviceId = tokens[0];
            string scopeId = tokens[1];
            string primaryKey = tokens[2];
            string secondaryKey = tokens[3];
            string gatewayHostName = tokens[4];

            // check if we got either a Fully Qualified Domain Name (FQDN) or an IP
            if (!gatewayHostName.Contains('.'))
            {
                Log.Logger.Warning(
                    "The configured host name '{gatewayHostName}' is not a fully qualified domain name or an IP. " +
                    "This is only recommended for development purposes!!! " +
                    "For productive workloads please configure a fully qualified domain name " +
                    "(FQDN, i.e. in hostname.domain format) or an IP for the Edge gateway.",
                    gatewayHostName);
            }

            Log.Logger.Information("Trying to find Edge Hub on gateway: {gatewayHostName}", gatewayHostName);
            try
            {
                using SecurityProviderSymmetricKey securityKey = new SecurityProviderSymmetricKey(deviceId, primaryKey, secondaryKey);
                var transportHandler = new ProvisioningTransportHandlerMqtt(TransportFallbackType.TcpWithWebSocketFallback);
                if (Environment.GetEnvironmentVariable("WEB_PROXY_CONFIG") != null)
                {
                    transportHandler.Proxy = new WebProxy(Environment.GetEnvironmentVariable("WEB_PROXY_URL"));
                }; 

                ProvisioningDeviceClient provisioningClient = ProvisioningDeviceClient.Create("global.azure-devices-provisioning.net", scopeId, securityKey, transportHandler);
                DeviceRegistrationResult result = provisioningClient.RegisterAsync().GetAwaiter().GetResult();

                string iotCentralconnectionString = $"HostName={result.AssignedHub};DeviceId={result.DeviceId};SharedAccessKey={primaryKey};GatewayHostName={ gatewayHostName.ToLower()}";

                List<ITransportSettings> transportSettingsList = new List<ITransportSettings>();
                MqttTransportSettings transportSettings = new MqttTransportSettings(TransportType.Mqtt_WebSocket_Only)
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
                };
                transportSettingsList.Add(transportSettings);

                return DeviceClient.CreateFromConnectionString(iotCentralconnectionString, transportSettingsList.ToArray());
            }
            catch (AggregateException ex)
            {
                foreach (var innerException in ex.Flatten().InnerExceptions)
                {
                    Log.Logger.Error(innerException, innerException.Message);
                }
                Log.Logger.Error("Failed to create device client using DPS.");

                return null;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Failed to create device client using DPS: {message}", ex.Message);

                return null;
            }
        }

        private async Task RegisterHandlersAsync(DeviceClient client, CancellationToken cancellationToken = default)
        {
            Log.Logger.Information("Registering method handlers and callbacks");

            // register method handlers
            await client.SetMethodHandlerAsync("Write", HandleWriteMethodAsync, client, cancellationToken).ConfigureAwait(false);
            await client.SetMethodHandlerAsync("Command", HandleCommandMethodAsync, client, cancellationToken).ConfigureAwait(false);

            // register default handler for everything else
            await client.SetMethodDefaultHandlerAsync(DefaultMethodHandlerAsync, client, cancellationToken).ConfigureAwait(false);

            // register connection status changed handler
            client.SetConnectionStatusChangesHandler(ConnectionStatusChange);
        }

        private async Task RegisterHandlersAsync(ModuleClient client, CancellationToken cancellationToken = default)
        {
            Log.Logger.Information("Registering method handlers and callbacks");

            // register method handlers
            await client.SetMethodHandlerAsync("Write", HandleWriteMethodAsync, client, cancellationToken).ConfigureAwait(false);
            await client.SetMethodHandlerAsync("Command", HandleCommandMethodAsync, client, cancellationToken).ConfigureAwait(false);

            // register default method handler for everything else
            await client.SetMethodDefaultHandlerAsync(DefaultMethodHandlerAsync, client, cancellationToken).ConfigureAwait(false);

            // register connection status changed handler
            client.SetConnectionStatusChangesHandler(ConnectionStatusChange);
        }

        public Task<MethodResponse> HandleCommandMethodAsync(MethodRequest methodRequest, object userContext)
        {
            try
            {
                new UAClient().ExecuteUACommand(_appConfig, methodRequest.DataAsJson);
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject("Success")), (int)HttpStatusCode.OK));
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "HandleCommandMethodAsync");
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ex.Message)), (int)HttpStatusCode.InternalServerError));
            }
        }

        public Task<MethodResponse> HandleWriteMethodAsync(MethodRequest methodRequest, object userContext)
        {
            try
            { 
                new UAClient().WriteUAVariable(_appConfig, methodRequest.DataAsJson);
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject("Success")), (int)HttpStatusCode.OK));
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "HandleWriteMethodAsync");
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ex.Message)), (int)HttpStatusCode.InternalServerError));
            }
        }

        public Task<MethodResponse> DefaultMethodHandlerAsync(MethodRequest methodRequest, object userContext)
        {
            string errorMessage = $"Method '{methodRequest.Name}' successfully received, but this method is not implemented!";
            Log.Logger.Information("DefaultMethodHandlerAsync: {errorMessage}", errorMessage);
            return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(errorMessage)), (int)HttpStatusCode.NotImplemented));
        }
    }
}
