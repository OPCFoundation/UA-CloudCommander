namespace Opc.Ua.Cloud.Commander
{
    using Opc.Ua;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    /// <summary>
    /// Parses spec-compliant OPC UA PubSub ActionRequest NetworkMessages (OPC 10000-14, 7.2.5.6),
    /// executes the requested OPC UA operations and builds the matching ActionResponse
    /// NetworkMessage. Shared by all transport bindings (MQTT, Kafka, NATS).
    /// </summary>
    internal static class PubSubActionHandler
    {
        private const ushort DefaultDataSetWriterId = 1;

        private static readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Processes a ua-action-request NetworkMessage and returns the serialized
        /// ua-action-response NetworkMessage together with the address to send it to.
        /// </summary>
        public static async Task<PubSubActionResult> ProcessRequestAsync(ApplicationConfiguration appConfiguration, string requestJson)
        {
            ActionNetworkMessage request;
            try
            {
                request = JsonSerializer.Deserialize<ActionNetworkMessage>(requestJson, _serializerOptions);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Received message is not a valid OPC UA PubSub Action NetworkMessage.");
                return PubSubActionResult.NoResponse;
            }

            if ((request == null) || (request.MessageType != ActionMessageTypes.Request))
            {
                Log.Logger.Warning("Ignoring message that is not a ua-action-request NetworkMessage.");
                return PubSubActionResult.NoResponse;
            }

            // discard stale requests based on the Requestor's TimeoutHint (default 15 seconds)
            TimeSpan timeout = (request.TimeoutHint.HasValue && (request.TimeoutHint.Value > 0))
                ? TimeSpan.FromMilliseconds(request.TimeoutHint.Value)
                : TimeSpan.FromSeconds(15);

            if ((request.Timestamp != default) && (request.Timestamp < DateTime.UtcNow.Subtract(timeout)))
            {
                Log.Logger.Information("Discarding expired ActionRequest with timestamp {Timestamp}.", request.Timestamp);
                return PubSubActionResult.NoResponse;
            }

            List<ActionDataSetMessage> responseMessages = new();
            if (request.Messages != null)
            {
                foreach (ActionDataSetMessage requestMessage in request.Messages)
                {
                    responseMessages.Add(await ExecuteActionAsync(appConfiguration, requestMessage).ConfigureAwait(false));
                }
            }

            // build the ua-action-response NetworkMessage, echoing the correlation values (OPC 10000-14, Table 195)
            ActionNetworkMessage response = new()
            {
                MessageId = Guid.NewGuid().ToString(),
                MessageType = ActionMessageTypes.Response,
                PublisherId = ResponderId,
                Timestamp = DateTime.UtcNow,
                RequestorId = request.RequestorId,
                CorrelationData = request.CorrelationData,
                Messages = responseMessages
            };

            return new PubSubActionResult
            {
                ShouldRespond = true,
                ResponseAddress = request.ResponseAddress,
                ResponseJson = JsonSerializer.Serialize(response, _serializerOptions)
            };
        }

        private static async Task<ActionDataSetMessage> ExecuteActionAsync(ApplicationConfiguration appConfiguration, ActionDataSetMessage request)
        {
            ActionDataSetMessage response = new()
            {
                DataSetWriterId = (request.DataSetWriterId != 0) ? request.DataSetWriterId : DefaultDataSetWriterId,
                ActionTargetId = request.ActionTargetId,
                MessageType = ActionMessageTypes.Response,
                RequestId = request.RequestId,
                Timestamp = DateTime.UtcNow,
                ActionState = ActionState.Done
            };

            // the Payload of an ActionRequest carries the OPC UA-encoded operation parameters
            string payloadJson = (request.Payload.ValueKind == JsonValueKind.Object)
                ? request.Payload.GetRawText()
                : "{}";

            try
            {
                UAClient client = new();
                string result = string.Empty;

                switch ((CommanderActionTarget)request.ActionTargetId)
                {
                    case CommanderActionTarget.Read:
                        result = await client.ReadUAVariableAsync(appConfiguration, payloadJson).ConfigureAwait(false);
                        break;
                    case CommanderActionTarget.HistoricalRead:
                        result = await client.ReadUAHistoryAsync(appConfiguration, payloadJson).ConfigureAwait(false);
                        break;
                    case CommanderActionTarget.Write:
                        await client.WriteUAVariableAsync(appConfiguration, payloadJson).ConfigureAwait(false);
                        break;
                    case CommanderActionTarget.MethodCall:
                        result = await client.ExecuteUACommandAsync(appConfiguration, payloadJson).ConfigureAwait(false);
                        break;
                    default:
                        throw new ServiceResultException(StatusCodes.BadNotSupported, $"Unknown ActionTargetId {request.ActionTargetId}.");
                }

                response.Status = StatusCodes.Good;
                response.Payload = JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["Result"] = result }, _serializerOptions);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Executing ActionTargetId {ActionTargetId} failed.", request.ActionTargetId);
                response.Status = (ex is ServiceResultException sre) ? sre.StatusCode : StatusCodes.Bad;
                response.Payload = JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["Error"] = ex.Message }, _serializerOptions);
            }

            return response;
        }

        // PublisherId of UA Cloud Commander acting as the Responder for Actions.
        private static string ResponderId =>
            Environment.GetEnvironmentVariable("CLIENTNAME")
            ?? Environment.GetEnvironmentVariable("APPNAME")
            ?? "UACloudCommander";
    }
}
