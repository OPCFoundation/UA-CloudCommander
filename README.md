# UACommander
An OPC UA industrial gateway Docker container for executing commands and writes on on-prem OPC UA servers.

## Configuration
The following environment variables are supported:
* METHOD_HANDLER - Method handler to use: "IoTHubMethodHandler" (default) or "MQTTClientMethodHandler" (optional)
* CONNECTION_STRING - Connection string to the method handler
* RUN_AS_DEVICE - Flag, if defined, to indicate if UA Commander should run as an IoT Hub device instead of an IoT Edge module
* WEB_PROXY_URL - Url of a web proxy server to use
* LOG_FILE_PATH - Path to use for the log file
* MQTT_USERNAME - Username for the optional MQTT broker
* MQTT_PASSWORD - Password for the optional MQTT broker
* UA_USERNAME - Optional username for the OPC UA server to connect to
* UA_PASSWORD - Optional password for the OPC UA server to connect to

## Usage
MQTT support is currently experimental. IoT Hub support can be leveraged by calling "Direct Methods". The currently supported functionality is:

### Method "Write"
Writes a UANode on an OPC UA server that must be in the UA Commander's network, example parameters:
```json
{
    "Endpoint": "opc.tcp://myopcserver.contoso/UA/",
    "NodeId": "http://opcfoundation.org/UA/Station/;i=123",
    "ValueToWrite": {
        "Type": 6,
        "Body": 123
    }
}
```
The Body is the value and the Type number can be looked-up in the table [here](https://reference.opcfoundation.org/v104/Core/docs/Part6/5.1.2/).

### Method "Command"
Executes a command on an OPC UA server that must be in the UA Commander's network, example parameters:
```json
{
    "Endpoint": "opc.tcp://myopcserver.contoso/UA/",
    "MethodNodeId": "http://opcfoundation.org/UA/Station/;i=123",
    "ParentNodeId": "http://opcfoundation.org/UA/Station/;i=120",
    "Arguments": [
        {
            "Type": 6,
            "Body": 123
        },
        {
            "Type": 12,
            "Body": "hello"
        },
        {
            "Type": 10,
            "Body": 0.4
        }
    ]
}
```
Again, the Body is the value and the Type number can be looked-up in the table [here](https://reference.opcfoundation.org/v104/Core/docs/Part6/5.1.2/).
