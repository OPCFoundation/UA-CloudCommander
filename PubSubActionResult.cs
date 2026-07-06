namespace Opc.Ua.Cloud.Commander
{
    /// <summary>
    /// Outcome of processing an incoming OPC UA PubSub ActionRequest NetworkMessage.
    /// </summary>
    internal sealed class PubSubActionResult
    {
        // False when the incoming message was not a valid/timely ua-action-request and
        // therefore no ActionResponse should be published.
        public bool ShouldRespond { get; init; }

        // Address the Requestor asked responses to be sent to (ResponseAddress in the
        // ActionRequest). May be null, in which case the transport default is used.
        public string ResponseAddress { get; init; }

        // Serialized ua-action-response NetworkMessage.
        public string ResponseJson { get; init; }

        public static PubSubActionResult NoResponse { get; } = new() { ShouldRespond = false };
    }
}
