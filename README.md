# UACommander
An OPC UA industrial gateway Docker container for executing commands, reads and writes on on-prem OPC UA servers from the cloud via an MQTT broker (including Azure IoT Hub).

## Configuration
The following environment variables are supported:
* LOG_FILE_PATH - Path to use for the log file to use
* CERT_STORE_PATH - Path to OPC UA certificate store to use
* MQTT_BROKERNAME - MQTT broker name to connect to
* MQTT_CLIENTNAME - MQTT client name, for example the device ID UACommander is running on. If running as an Azure IoT Edge module, this is <deviceID>/<moduleID>
* MQTT_TOPIC - Topic to subscribe to. "Read", "Write" and "Command" must be sub-topics of this topic, for IoT Hub, this is $iothub/methods/POST/#
* MQTT_RESPONSE_TOPIC - Topic to send responses to, for IoT Hub, this is $iothub/methods/res/{status}/?$rid={request id}
* MQTT_USERNAME - Username for the MQTT broker, for IoT Hub, this is <bokername>/<clientname>/?api-version=2018-06-30
* MQTT_PASSWORD - Password for the MQTT broker, for IoT Hub, this is the shared key of the client
* CREATE_SAS_PASSWORD - Create a SAS token from the password, this is for example needed when using IoT Hub as the MQTT broker
* UA_USERNAME - Optional username for the OPC UA server to connect to
* UA_PASSWORD - Optional password for the OPC UA server to connect to

## Usage
Execute:
```
docker run ghcr.io/barnstee/uacommander:main
```
from a Docker-enabled PC or Linux box.
Alternatively, deploy it as an Azure IoT Edge module from the Azure portal.

## Functionality
### Sub-Topic "Read"
Reads a UANode on an OPC UA server that must be in the UA Commander's network, example parameters:
```json
{
    "Endpoint": "opc.tcp://myopcserver.contoso/UA/",
    "NodeId": "http://opcfoundation.org/UA/Station/;i=123"
}
```

### Sub-Topic "Write"
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
The Body is the value and the associated Type can be looked-up in the table [here](https://reference.opcfoundation.org/v104/Core/docs/Part6/5.1.2/).

### Sub-Topic "Command"
Executes a command on an OPC UA server that must be in the UA Commander's network, example parameters:
```json
{
    "Endpoint": "opc.tcp://myopcserver.contoso/UA/",
    "MethodNodeId": "http://opcfoundation.org/UA/Station/;i=124",
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
Again, the Body is the value and the associated Type can be looked-up in the table [here](https://reference.opcfoundation.org/v104/Core/docs/Part6/5.1.2/).
    
## Docker Build Status
[![Docker](https://github.com/barnstee/UACommander/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/barnstee/UACommander/actions/workflows/docker-publish.yml)

