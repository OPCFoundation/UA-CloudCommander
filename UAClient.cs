
namespace UACommander
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Client.ComplexTypes;
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

                if (string.IsNullOrEmpty(expandedNodeID))
                {
                    Log.Logger.Error("Expanded node ID is not specified!");
                    throw new ArgumentException("Expanded node ID is not specified!");
                }

                ExpandedNodeId nodeID = ExpandedNodeId.Parse(expandedNodeID);

                string expandedParentNodeID = decoder.ReadString("ParentNodeId");

                if (string.IsNullOrEmpty(expandedParentNodeID))
                {
                    Log.Logger.Error("Expanded parent node ID is not specified!");
                    throw new ArgumentException("Expanded parent node ID is not specified!");
                }

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

        public string ReadUAVariable(ApplicationConfiguration appConfiguration, string payload)
        {
            Session session = null;

            try
            {
                JsonDecoder decoder = new JsonDecoder(payload, new ServiceMessageContext());

                string serverEndpoint = decoder.ReadString("Endpoint");
                session = CreateSession(appConfiguration, serverEndpoint);

                string expandedNodeID = decoder.ReadString("NodeId");

                if (string.IsNullOrEmpty(expandedNodeID))
                {
                    Log.Logger.Error("Expanded node ID is not specified!");
                    throw new ArgumentException("Expanded node ID is not specified!");
                }

                ExpandedNodeId nodeID = ExpandedNodeId.Parse(expandedNodeID);

                // read a variable node from the OPC UA server
                VariableNode node = (VariableNode)session.ReadNode(ExpandedNodeId.ToNodeId(nodeID, session.NamespaceUris));

                // load complex type system
                ComplexTypeSystem complexTypeSystem = new ComplexTypeSystem(session);
                ExpandedNodeId nodeTypeId = node.DataType;
                complexTypeSystem.LoadType(nodeTypeId).GetAwaiter().GetResult();

                // now that we have loaded the (potentionally) complex type, we can read the value
                DataValue value = session.ReadValue(ExpandedNodeId.ToNodeId(nodeID, session.NamespaceUris));
                if (StatusCode.IsBad(value.StatusCode))
                {
                    throw ServiceResultException.Create(value.StatusCode.Code, "Reading OPC UA node failed!");
                }

                return value.WrappedValue.ToString();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Reading OPC UA node failed!");
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
                
                if (string.IsNullOrEmpty(expandedNodeID))
                {
                    Log.Logger.Error("Expanded node ID is not specified!");
                    throw new ArgumentException("Expanded node ID is not specified!");
                }

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
            if (string.IsNullOrEmpty(serverEndpoint))
            {
                Log.Logger.Error("Server endpoint is not specified!");
                throw new ArgumentException("Server endpoint is not specified!");
            }

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
