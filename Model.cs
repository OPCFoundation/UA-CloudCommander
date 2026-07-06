
namespace Opc.Ua.Cloud.Commander
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// MessageType values for OPC UA PubSub Action NetworkMessages (OPC 10000-14, 7.2.2).
    /// </summary>
    internal static class ActionMessageTypes
    {
        public const string Request = "ua-action-request";

        public const string Response = "ua-action-response";
    }

    /// <summary>
    /// State of an Action execution (OPC 10000-14, Table 81).
    /// </summary>
    internal enum ActionState
    {
        Idle = 0,

        Executing = 1,

        Done = 2
    }

    /// <summary>
    /// Action targets offered by UA Cloud Commander acting as the Responder. The numeric
    /// ActionTargetId is set by a Requestor in an ActionRequest to select the operation.
    /// A full implementation would advertise these targets via ua-action-metadata.
    /// </summary>
    internal enum CommanderActionTarget : ushort
    {
        Read = 1,

        HistoricalRead = 2,

        Write = 3,

        MethodCall = 4
    }

    /// <summary>
    /// JSON Action NetworkMessage (OPC 10000-14, Table 195). Carries one or more
    /// ActionRequest or ActionResponse messages in the <see cref="Messages"/> array.
    /// </summary>
    internal sealed class ActionNetworkMessage
    {
        public string MessageId { get; set; }

        public string MessageType { get; set; }

        public string PublisherId { get; set; }

        public DateTime Timestamp { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ResponseAddress { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte[] CorrelationData { get; set; }

        public string RequestorId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? TimeoutHint { get; set; }

        public List<ActionDataSetMessage> Messages { get; set; }
    }

    /// <summary>
    /// JSON ActionRequest / ActionResponse DataSetMessage (OPC 10000-14, Table 196 / 197).
    /// </summary>
    internal sealed class ActionDataSetMessage
    {
        public ushort DataSetWriterId { get; set; }

        public ushort ActionTargetId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string MessageType { get; set; }

        public ushort RequestId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? Timestamp { get; set; }

        public ActionState ActionState { get; set; }

        // Overall result of an ActionResponse (StatusCode). Omitted for ActionRequests.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public uint? Status { get; set; }

        // Name-value pairs specified by the ActionMetaData. For requests these are the
        // OPC UA-encoded operation parameters; for responses the operation result.
        public JsonElement Payload { get; set; }
    }
}
