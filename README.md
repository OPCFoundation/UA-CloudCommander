# UA Cloud Commander

A cross-platform OPC UA cloud command & control reference implementation leveraging MQTT. It runs in a Docker container and executing commands, reads and writes on on-prem OPC UA servers from the cloud.

## Configuration

The following environment variables are REQUIRED:

* MQTT_BROKERNAME - (required) MQTT broker name to connect to
* MQTT_CLIENTNAME - (required) MQTT client name, for example the device ID UA Cloud Commander is running on. If running as an Azure IoT Edge module, this is `<deviceID>/<moduleID>`
* MQTT_TOPIC - (required) Topic to subscribe to. "Read", "Write" and "Command" must be sub-topics of this topic, for IoT Hub, this is `$iothub/methods/POST/#`
* MQTT_RESPONSE_TOPIC - (required) Topic to send responses to, for IoT Hub, this is `$iothub/methods/res/`
* MQTT_USERNAME - (required) Username for the MQTT broker, for IoT Hub, this is `<brokername>/<clientname>/?api-version=2018-06-30`
* MQTT_PASSWORD - (required) Password for the MQTT broker, for IoT Hub, this is the shared primary key of the client

The following environment variables are optional:

* LOG_FILE_PATH - (optional) Path to use for the log file to use
* CERT_STORE_PATH - (optional) Path to OPC UA certificate store to use
* CREATE_SAS_PASSWORD - (optional) Create a SAS token from the password, this is for example needed when using IoT Hub as the MQTT broker
* UA_USERNAME - (optional) username for the OPC UA server to connect to
* UA_PASSWORD - (optional) password for the OPC UA server to connect to

## Usage

Execute:

```shell
docker run --env-file .env.local ghcr.io/barnstee/ua-cloudcommander:main
```

from a Docker-enabled PC or Linux box. Use [.env.local](.env.local) with suitable values.

Alternatively, deploy it as an Azure IoT Edge module from the Azure portal.

## Sending Commands to UA Commander
From an MQTT client, commands can be sent to an MQTT broker UA Commander has been configured for. UA Commander subscribes to the configured MQTT topic to receive commands, executes them and reports command execution status via the configured MQTT response topic.

The topic must include either Read, Write or Command as well as a request ID in the form {MQTT topic path}/{command name}/?$rid={request id}, for example /myUAServer/Read/?$rid=123.

UA Commander will respond via the configured MQTT response topic in the form {MQTT topic path}/{status code}/?$rid={request id}, for example /myUAServer/response/200/?$rid=123. In this message, the request ID will match the one in the original command message.

### Read Payload

Reads a UANode on an OPC UA server that must be in the UA Cloud Commander's network, example parameters:

```json
{
    "Endpoint": "opc.tcp://myopcserver.contoso/UA/",
    "NodeId": "http://opcfoundation.org/UA/Station/;i=123"
}
```

### Write Payload

Writes a UANode on an OPC UA server that must be in the UA Cloud Commander's network, example parameters:

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

### Command Payload

Executes a command on an OPC UA server that must be in the UA Cloud Commander's network, example parameters:

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

[![Docker](https://github.com/barnstee/UA-CloudCommander/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/barnstee/UA-CloudCommander/actions/workflows/docker-publish.yml)
