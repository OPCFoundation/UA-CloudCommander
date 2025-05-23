# UA Cloud Commander

A cross-platform, .Net9.0-based OPC UA command & control reference implementation leveraging MQTT and Kafka. It runs in a Docker container on-premises (on Docker or Kubernetes) and executes OPC UA commands, reads and writes OPC UA variables and supports Historical Data Access (HDA) for on-premises OPC UA servers, executed from the cloud.

## Configuration

The following environment variables are REQUIRED:

* BROKERNAME - Broker name to connect to
* BROKERPORT = Broker port to connect to. When 443 is specified, Websockets are used.
* CLIENTNAME - Client name, for example the device ID UA Cloud Commander is running on. If running as an Azure IoT Edge module, this is `<deviceID>/<moduleID>`
* TOPIC - Topic to subscribe to in the syntax `<YourTopicName>/#`. `Read`, `Write` and `Command` must be sub-topics of this topic. For IoT Hub, the topic is `$iothub/methods/POST/#`
* RESPONSE_TOPIC - Topic to send responses to, for IoT Hub, this is `$iothub/methods/res/`
* USERNAME - Username for the broker, for IoT Hub, this is `<brokername>/<clientname>/?api-version=2018-06-30`
* PASSWORD - Password for the broker, for IoT Hub, this is the shared primary key of the client

The following environment variables are optional:

* CREATE_SAS_PASSWORD - Create a SAS token from the password, this is for example needed when using IoT Hub as the MQTT broker
* USE_TLS - Use TLS (usually, you also need to change the port from 1883 to 8883)
* USE_UA_CERT_AUTH - Use the UA Certificate to authenticate with the MQTT broker
* UA_USERNAME - Username for the OPC UA server to connect to
* UA_PASSWORD - Password for the OPC UA server to connect to
* USE_KAFKA - Use Kafka instead of MQTT for communication
* STORAGE_CONNECTION_STRING - The connection string to the cloud-based OPC UA certificate store.
* STORAGE_CONTAINER_NAME - The cloud-based OPC UA certificate store container name.
* APPNAME - An alternative name for UA Cloud Commander.

## Usage

Execute:

```shell
docker run --env-file .env.local ghcr.io/opcfoundation/ua-cloudcommander:main
```

from a Docker-enabled PC or Linux box. Use [.env.local](.env.local) with suitable values.

Alternatively, deploy it as an Azure IoT Edge module from the Azure portal.

## Sending Commands to UA Commander
From an broker client, commands can be sent to a broker UA Commander has been configured for. UA Commander subscribes to the configured broker topic to receive commands, executes them and reports command execution status via the configured broker response topic.

The topic must include either Read, Write or Command as well as a request ID in the form {broker topic path}/{command name}/?$rid={request id}, for example /myUAServer/Read/?$rid=123.

UA Commander will respond via the configured broker response topic in the form {broker topic path}/{status code}/?$rid={request id}, for example /myUAServer/response/200/?$rid=123. In this message, the request ID will match the one in the original command message.

### `Read` Command Payload

Reads a UA Node on an OPC UA server that must be in the UA Cloud Commander's network, example parameters:

```json
{
    "Command": "Read",
    "CorrelationId": "D892A987-56FB-4724-AF14-5EC6A7EBDD07", // a GUID
    "TimeStamp": "2022-11-28T12:01:00.0923534Z", // sender timestamp in UTC
    "Endpoint": "opc.tcp://myopcserver.contoso/UA/",
    "NodeId": "http://opcfoundation.org/UA/Station/;i=123"
}
```

### `HistoricalRead` Command (HDA) Payload

Reads the histroy for a UA Node on an OPC UA server that must be in the UA Cloud Commander's network, example parameters:

```json
{
    "Command": "HistoricalRead",
    "CorrelationId": "D892A987-56FB-4724-AF14-5EC6A7EBDD07", // a GUID
    "TimeStamp": "2022-11-28T12:01:00.0923534Z", // sender timestamp in UTC
    "Endpoint": "opc.tcp://myopcserver.contoso/UA/",
    "NodeId": "http://opcfoundation.org/UA/Station/;i=123"
    "StartTime": "2022-11-28T12:00:00.0923534Z" // start time for historical values
    "EndTime": "2022-11-28T12:01:00.0923534Z" // end time for historical values
}
```

### `Write` Command Payload

Writes a UA Node on an OPC UA server that must be in the UA Cloud Commander's network, example parameters:

```json
{
    "Command": "Write",
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

### `MethodCall` Command Payload

Executes a command on an OPC UA server that must be in the UA Cloud Commander's network, example parameters:

```json
{
    "Command": "MethodCall",
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
