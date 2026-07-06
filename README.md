# UA Cloud Commander

A cross-platform, .Net-based OPC UA command & control reference implementation leveraging MQTT, NATS and Kafka. It runs in a Docker container on-premises (on Docker or Kubernetes) and executes OPC UA PubSub Actions (methods, reads and writes of OPC UA variables) and supports Historical Data Access (HDA) for on-premises OPC UA servers, executed from the cloud.

## Configuration

The following environment variables are REQUIRED:

* BROKERNAME - Broker name to connect to
* BROKERPORT = Broker port to connect to. When 443 is specified, Websockets are used.
* CLIENTNAME - Client name, for example the device ID UA Cloud Commander is running on. If running as an Azure IoT Edge module, this is `<deviceID>/<moduleID>`
* TOPIC - Topic to subscribe to for incoming `ua-action-request` NetworkMessages, in the syntax `<YourTopicName>/#`. For IoT Hub, the topic is `$iothub/methods/POST/#`
* RESPONSE_TOPIC - Default topic to publish `ua-action-response` NetworkMessages to when a request does not specify a ResponseAddress. For IoT Hub, this is `$iothub/methods/res/`
* USERNAME - Username for the broker, for IoT Hub, this is `<brokername>/<clientname>/?api-version=2018-06-30`
* PASSWORD - Password for the broker, for IoT Hub, this is the shared primary key of the client

The following environment variables are optional:

* CREATE_SAS_PASSWORD - Create a SAS token from the password, this is for example needed when using IoT Hub as the MQTT broker
* USE_TLS - Use TLS (usually, you also need to change the port from 1883 to 8883)
* USE_UA_CERT_AUTH - Use the UA Certificate to authenticate with the MQTT broker
* UA_USERNAME - Username for the OPC UA server to connect to
* UA_PASSWORD - Password for the OPC UA server to connect to
* USE_KAFKA - Use Kafka instead of MQTT for communication
* USE_NATS - Use NATS instead of MQTT for communication
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

## Sending Actions to UA Commander

UA Cloud Commander implements the OPC UA PubSub **Actions** request/response pattern defined in [OPC 10000-14](https://reference.opcfoundation.org/Core/Part14/) (PubSub). It acts as the **Responder**: a cloud application (the **Requestor**) publishes a `ua-action-request` NetworkMessage to the configured `TOPIC`, UA Cloud Commander executes the requested OPC UA operation and publishes a `ua-action-response` NetworkMessage back (to the request's `ResponseAddress`, or to `RESPONSE_TOPIC` if none is provided).

The operation to perform is selected via the `ActionTargetId` of each message contained in the NetworkMessage:

| ActionTargetId | Operation | Description |
| -------------- | --------- | ----------- |
| 1 | Read | Reads a UA Node's value |
| 2 | HistoricalRead | Reads a UA Node's history (HDA) |
| 3 | Write | Writes a UA Node's value |
| 4 | MethodCall | Calls a UA Method |

### Action Request NetworkMessage

The `ua-action-request` NetworkMessage envelope (OPC 10000-14, 7.2.5.6) carries the correlation information; the OPC UA operation parameters are placed in the `Payload` of each message. Example for a `Read`:

```json
{
    "MessageId": "32235f26-4a3a-4a56-9f1f-2b6f8a2f0a11", // a globally unique id for the message
    "MessageType": "ua-action-request",
    "PublisherId": "MyUACloudCommander", // the Responder (UA Cloud Commander) PublisherId, i.e. CLIENTNAME
    "Timestamp": "2022-11-28T12:01:00.0923534Z", // sender timestamp in UTC
    "ResponseAddress": "myResponseTopic", // where to publish the response (falls back to RESPONSE_TOPIC if omitted)
    "CorrelationData": "0RKKl/tHJHSvFF7Gp+vdBw==", // base64 data echoed back in the response to correlate it
    "RequestorId": "MyCloudApp", // the Requestor PublisherId
    "TimeoutHint": 15000, // milliseconds after which the Responder stops processing the request
    "Messages": [
        {
            "DataSetWriterId": 1,
            "ActionTargetId": 1, // 1 = Read
            "RequestId": 1, // echoed back in the response
            "ActionState": 1, // 1 = Executing
            "Payload": {
                "Endpoint": "opc.tcp://myopcserver.contoso/UA/",
                "NodeId": "http://opcfoundation.org/UA/Station/;i=123"
            }
        }
    ]
}
```

### Action Target Payloads

The `Payload` object of each request message depends on the `ActionTargetId`.

#### Read (`ActionTargetId` 1)

Reads a UA Node on an OPC UA server that must be in the UA Cloud Commander's network:

```json
"Payload": {
    "Endpoint": "opc.tcp://myopcserver.contoso/UA/",
    "NodeId": "http://opcfoundation.org/UA/Station/;i=123"
}
```

#### HistoricalRead (`ActionTargetId` 2)

Reads the history (HDA) for a UA Node on an OPC UA server that must be in the UA Cloud Commander's network:

```json
"Payload": {
    "Endpoint": "opc.tcp://myopcserver.contoso/UA/",
    "NodeId": "http://opcfoundation.org/UA/Station/;i=123",
    "StartTime": "2022-11-28T12:00:00.0923534Z", // start time for historical values
    "EndTime": "2022-11-28T12:01:00.0923534Z" // end time for historical values
}
```

#### Write (`ActionTargetId` 3)

Writes a UA Node on an OPC UA server that must be in the UA Cloud Commander's network:

```json
"Payload": {
    "Endpoint": "opc.tcp://myopcserver.contoso/UA/",
    "NodeId": "http://opcfoundation.org/UA/Station/;i=123",
    "ValueToWrite": {
        "Type": 6,
        "Body": 123
    }
}
```

The Body is the value and the associated Type can be looked-up in the table [here](https://reference.opcfoundation.org/v104/Core/docs/Part6/5.1.2/).

#### MethodCall (`ActionTargetId` 4)

Executes a Method on an OPC UA server that must be in the UA Cloud Commander's network:

```json
"Payload": {
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

### Action Response NetworkMessage

UA Cloud Commander replies with a `ua-action-response` NetworkMessage that echoes the `RequestorId`, `CorrelationData` and `RequestId` so the Requestor can correlate it. Each message carries a `Status` (OPC UA `StatusCode`, `0` = Good) and a `Payload` holding the operation `Result` (or an `Error` message on failure):

```json
{
    "MessageId": "b0a3f9d2-6d3a-4b3f-9c7e-1f2a3b4c5d6e",
    "MessageType": "ua-action-response",
    "PublisherId": "MyUACloudCommander",
    "Timestamp": "2022-11-28T12:01:00.5123534Z",
    "CorrelationData": "0RKKl/tHJHSvFF7Gp+vdBw==",
    "RequestorId": "MyCloudApp",
    "Messages": [
        {
            "DataSetWriterId": 1,
            "ActionTargetId": 1,
            "MessageType": "ua-action-response",
            "RequestId": 1,
            "ActionState": 2, // 2 = Done
            "Status": 0, // OPC UA StatusCode, 0 = Good
            "Payload": {
                "Result": "123"
            }
        }
    ]
}
```

## Docker Build Status

[![Docker](https://github.com/barnstee/UA-CloudCommander/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/barnstee/UA-CloudCommander/actions/workflows/docker-publish.yml)
