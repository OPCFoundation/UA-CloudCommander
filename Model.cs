
namespace Opc.Ua.Cloud.Commander
{
    using System;

    class RequestModel
    {
        public Guid CorrelationId { get; set; }

        public DateTime TimeStamp { get; set; }
    }

    class ResponseModel
    {
        public Guid CorrelationId { get; set; }

        public DateTime TimeStamp { get; set; }

        public bool Success { get; set; }

        public string Status { get; set; }
    }
}
