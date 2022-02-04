
namespace UACommander
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using Serilog;
    using System;

    public class UAClient
    {
        public void ExecuteUACommand(ApplicationConfiguration appConfiguration, string payload)
        {
            Session session = null;

            try
            {
                JsonDecoder decoder = new JsonDecoder(payload, new ServiceMessageContext());

                string serverEndpoint = decoder.ReadString("Endpoint");
                session = CreateSession(appConfiguration, serverEndpoint);

                string expandedNodeID = decoder.ReadString("MethodNodeId");
                ExpandedNodeId nodeID = ExpandedNodeId.Parse(expandedNodeID);

                string expandedParentNodeID = decoder.ReadString("ParentNodeId");
                ExpandedNodeId parentNodeID = ExpandedNodeId.Parse(expandedParentNodeID);

                CallMethodRequest request = new CallMethodRequest
                {
                    ObjectId = new NodeId(parentNodeID.Identifier, parentNodeID.NamespaceIndex),
                    MethodId = new NodeId(nodeID.Identifier, nodeID.NamespaceIndex),
                };

                request.InputArguments = decoder.ReadVariantArray("Arguments");
   
                CallMethodRequestCollection requests = new CallMethodRequestCollection
                {
                    request
                };

                CallMethodResultCollection results;
                DiagnosticInfoCollection diagnosticInfos;

                ResponseHeader responseHeader = session.Call(
                    null,
                    requests,
                    out results,
                    out diagnosticInfos);

                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    throw new ServiceResultException(new ServiceResult(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable));
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Executing OPC UA command failed!");
                throw ex;
            }
            finally
            {
                if (session != null)
                {
                    if (session.Connected)
                    {
                        session.Close();
                    }

                    session.Dispose();
                }
            }
        }

        public void WriteUAVariable(ApplicationConfiguration appConfiguration, string payload)
        {
            Session session = null;

            try
            {
                JsonDecoder decoder = new JsonDecoder(payload, new ServiceMessageContext());

                string serverEndpoint = decoder.ReadString("Endpoint");
                session = CreateSession(appConfiguration, serverEndpoint);

                string expandedNodeID = decoder.ReadString("NodeId");
                ExpandedNodeId nodeID = ExpandedNodeId.Parse(expandedNodeID);
                                
                WriteValue nodeToWrite = new WriteValue()
                {
                    NodeId = new NodeId(nodeID.Identifier, nodeID.NamespaceIndex),
                    AttributeId = Attributes.Value,
                    Value = new DataValue()
                };
                nodeToWrite.Value.WrappedValue = decoder.ReadVariant("ValueToWrite");

                WriteValueCollection nodesToWrite = new WriteValueCollection() {
                    nodeToWrite
                };
                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                ResponseHeader responseHeader = session.Write(
                    null,
                    nodesToWrite,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToWrite);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToWrite);

                if (StatusCode.IsBad(results[0]))
                {
                    throw ServiceResultException.Create(results[0], 0, diagnosticInfos, responseHeader.StringTable);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Writing OPC UA node failed!");
                throw ex;
            }
            finally
            {
                if (session != null)
                {
                    if (session.Connected)
                    {
                        session.Close();
                    }

                    session.Dispose();
                }
            }
        }

        private Session CreateSession(ApplicationConfiguration appConfiguration, string serverEndpoint)
        {
            // find endpoint on a local OPC UA server
            EndpointDescription endpointDescription = CoreClientUtils.SelectEndpoint(serverEndpoint, true);
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(appConfiguration);
            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            // check which identity to use
            UserIdentity userIdentity = new UserIdentity(new AnonymousIdentityToken());
            if ((Environment.GetEnvironmentVariable("UA_USERNAME") != null) && (Environment.GetEnvironmentVariable("UA_PASSWORD") != null))
            {
                userIdentity = new UserIdentity(Environment.GetEnvironmentVariable("UA_USERNAME"), Environment.GetEnvironmentVariable("UA_PASSWORD"));
            }

            Log.Logger.Information("Creating secure session for endpoint {endpointUrl}.", endpoint.EndpointUrl);

            Session session = Session.Create(
                appConfiguration,
                endpoint,
                false,
                false,
                appConfiguration.ApplicationName,
                (uint)appConfiguration.ClientConfiguration.DefaultSessionTimeout,
                userIdentity,
                null)
                .GetAwaiter().GetResult();
            if (!session.Connected)
            {
                Log.Logger.Error("Connection to OPC UA server failed!");
                return null;
            }
            else
            {
                return session;
            }
        }
    }
}
