# UA Cloud Commander

A cross-platform OPC UA cloud command & control reference implementation leveraging MQTT and Kafka. It runs in a Docker container and executing commands, reads and writes on on-prem OPC UA servers from the cloud.

## Configuration

The following environment variables are REQUIRED:

* BROKERNAME - (required) broker name to connect to
* CLIENTNAME - (required) client name, for example the device ID UA Cloud Commander is running on. If running as an Azure IoT Edge module, this is `<deviceID>/<moduleID>`
* TOPIC - (required) Topic to subscribe to in the syntax `<YourTopicName>/#`. `Read`, `Write` and `Command` must be sub-topics of this topic. For IoT Hub, the topic is `$iothub/methods/POST/#`
* RESPONSE_TOPIC - (required) Topic to send responses to, for IoT Hub, this is `$iothub/methods/res/`
* USERNAME - (required) Username for the broker, for IoT Hub, this is `<brokername>/<clientname>/?api-version=2018-06-30`
* PASSWORD - (required) Password for the broker, for IoT Hub, this is the shared primary key of the client

The following environment variables are optional:

* LOG_FILE_PATH - (optional) Path to use for the log file to use
* CERT_STORE_PATH - (optional) Path to OPC UA certificate store to use
* CREATE_SAS_PASSWORD - (optional) Create a SAS token from the password, this is for example needed when using IoT Hub as the MQTT broker
* UA_USERNAME - (optional) username for the OPC UA server to connect to
* UA_PASSWORD - (optional) password for the OPC UA server to connect to
* USE_KAFKA - (optional) use Kafka instead of MQTT for communication

## Usage

Execute:

```shell
docker run --env-file .env.local ghcr.io/barnstee/ua-cloudcommander:main
```

from a Docker-enabled PC or Linux box. Use [.env.local](.env.local) with suitable values.

Alternatively, deploy it as an Azure IoT Edge module from the Azure portal.

## Sending Commands to UA Commander
From an broker client, commands can be sent to a broker UA Commander has been configured for. UA Commander subscribes to the configured broker topic to receive commands, executes them and reports command execution status via the configured broker response topic.

The topic must include either Read, Write or Command as well as a request ID in the form {broker topic path}/{command name}/?$rid={request id}, for example /myUAServer/Read/?$rid=123.

UA Commander will respond via the configured broker response topic in the form {broker topic path}/{status code}/?$rid={request id}, for example /myUAServer/response/200/?$rid=123. In this message, the request ID will match the one in the original command message.

### Read Payload

Reads a UANode on an OPC UA server that must be in the UA Cloud Commander's network, example parameters:

```json
{
    "Command": "read",
    "CorrelationId": "D892A987-56FB-4724-AF14-5EC6A7EBDD07", // a GUID
    "TimeStamp": "2022-11-28T12:01:00.0923534Z", // sender timestamp in UTC
    "Endpoint": "opc.tcp://myopcserver.contoso/UA/",
    "NodeId": "http://opcfoundation.org/UA/Station/;i=123"
}
```

### Write Payload

Writes a UANode on an OPC UA server that must be in the UA Cloud Commander's network, example parameters:

```json
{
    "Command": "write",
    "CorrelationId": "D892A987-56FB-4724-AF14-5EC6A7EBDD07", // a GUID
    "TimeStamp": "2022-11-28T12:01:00.0923534Z", // sender timestamp in UTC
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
    "Command": "methodcall",
    "CorrelationId": "D892A987-56FB-4724-AF14-5EC6A7EBDD07", // a GUID
    "TimeStamp": "2022-11-28T12:01:00.0923534Z", // sender timestamp in UTC
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

### UA Commander Response

UA Cloud Commander will match the correlation ID of the request, update the timestamp (in UTC) and give a status message on failure.

```json
{
    "CorrelationId": "D892A987-56FB-4724-AF14-5EC6A7EBDD07",
    "TimeStamp": "2022-11-28T12:01:00.0923534Z",
    "Success": TRUE,
    "Status": ""
}
```

## Docker Build Status

[![Docker](https://github.com/barnstee/UA-CloudCommander/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/barnstee/UA-CloudCommander/actions/workflows/docker-publish.yml)
