namespace Opc.Ua.Cloud.Commander
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Client.ComplexTypes;
    using Serilog;
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class UAClient
    {
        public async Task<string> ExecuteUACommandAsync(ApplicationConfiguration appConfiguration, string payload)
        {
            ISession session = null;

            try
            {
                using JsonDecoder decoder = new JsonDecoder(payload, new ServiceMessageContext(Program.Telemetry));

                string serverEndpoint = decoder.ReadString("Endpoint");
                session = await CreateSessionAsync(appConfiguration, serverEndpoint).ConfigureAwait(false);

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

                CallResponse response = await session.CallAsync(
                    null,
                    requests,
                    CancellationToken.None).ConfigureAwait(false);

                if (StatusCode.IsBad(response.Results[0].StatusCode))
                {
                    throw new ServiceResultException(new ServiceResult(response.Results[0].StatusCode, 0, response.DiagnosticInfos, response.ResponseHeader.StringTable));
                }

                // put the results in a comma-seperated string
                string result = string.Empty;
                if ((response.Results?.Count > 0) && (response.Results[0].OutputArguments != null) && (response.Results[0].OutputArguments.Count > 0))
                {
                    foreach (Variant argument in response.Results[0].OutputArguments)
                    {
                        result += argument.ToString() + ',';
                    }
                }

                return result.TrimEnd(',');
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Executing OPC UA command failed!");
                throw;
            }
            finally
            {
                if (session != null)
                {
                    if (session.Connected)
                    {
                        await session.CloseAsync().ConfigureAwait(false);
                    }

                    session.Dispose();
                }
            }
        }

        public async Task<string> ReadUAVariableAsync(ApplicationConfiguration appConfiguration, string payload)
        {
            ISession session = null;

            try
            {
                using JsonDecoder decoder = new JsonDecoder(payload, new ServiceMessageContext(Program.Telemetry));

                string serverEndpoint = decoder.ReadString("Endpoint");
                session = await CreateSessionAsync(appConfiguration, serverEndpoint).ConfigureAwait(false);

                string expandedNodeID = decoder.ReadString("NodeId");

                if (string.IsNullOrEmpty(expandedNodeID))
                {
                    Log.Logger.Error("Expanded node ID is not specified!");
                    throw new ArgumentException("Expanded node ID is not specified!");
                }

                ExpandedNodeId nodeID = ExpandedNodeId.Parse(expandedNodeID);

                // read a variable node from the OPC UA server
                VariableNode node = (VariableNode)await session.ReadNodeAsync(ExpandedNodeId.ToNodeId(nodeID, session.NamespaceUris)).ConfigureAwait(false);

                // load complex type system
                ComplexTypeSystem complexTypeSystem = new ComplexTypeSystem(session);
                ExpandedNodeId nodeTypeId = node.DataType;
                await complexTypeSystem.LoadTypeAsync(nodeTypeId).ConfigureAwait(false);

                // now that we have loaded the (potentionally) complex type, we can read the value
                DataValue value = await session.ReadValueAsync(ExpandedNodeId.ToNodeId(nodeID, session.NamespaceUris)).ConfigureAwait(false);
                if (StatusCode.IsBad(value.StatusCode))
                {
                    throw ServiceResultException.Create(value.StatusCode.Code, "Reading OPC UA node failed!");
                }

                return value.WrappedValue.ToString();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Reading OPC UA node failed!");
                throw;
            }
            finally
            {
                if (session != null)
                {
                    if (session.Connected)
                    {
                        await session.CloseAsync().ConfigureAwait(false);
                    }

                    session.Dispose();
                }
            }
        }

        public async Task<string> ReadUAHistoryAsync(ApplicationConfiguration appConfiguration, string payload)
        {
            ISession session = null;
            string result = string.Empty;

            try
            {
                using JsonDecoder decoder = new JsonDecoder(payload, new ServiceMessageContext(Program.Telemetry));

                string serverEndpoint = decoder.ReadString("Endpoint");
                session = await CreateSessionAsync(appConfiguration, serverEndpoint).ConfigureAwait(false);

                string expandedNodeID = decoder.ReadString("NodeId");
                DateTime startTime = decoder.ReadDateTime("StartTime");
                DateTime endTime = decoder.ReadDateTime("EndTime");


                if (string.IsNullOrEmpty(expandedNodeID))
                {
                    Log.Logger.Error("Expanded node ID is not specified!");
                    throw new ArgumentException("Expanded node ID is not specified!");
                }

                ExpandedNodeId nodeID = ExpandedNodeId.Parse(expandedNodeID);

                // read a variable node from the OPC UA server
                VariableNode node = (VariableNode)await session.ReadNodeAsync(ExpandedNodeId.ToNodeId(nodeID, session.NamespaceUris)).ConfigureAwait(false);

                // load complex type system
                ComplexTypeSystem complexTypeSystem = new(session);
                ExpandedNodeId nodeTypeId = node.DataType;
                await complexTypeSystem.LoadTypeAsync(nodeTypeId).ConfigureAwait(false);

                // now that we have loaded the (potentionally) complex type, we can read the history
                ReadRawModifiedDetails details = new()
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 0,
                    IsReadModified = false,
                    ReturnBounds = false
                };

                HistoryReadValueIdCollection nodesToRead = new();
                HistoryReadValueId nodeToRead = new()
                {
                    NodeId = ExpandedNodeId.ToNodeId(nodeID, session.NamespaceUris)
                };
                nodesToRead.Add(nodeToRead);

                HistoryReadResponse response = await session.HistoryReadAsync(
                    null,
                    new ExtensionObject(details),
                    TimestampsToReturn.Both,
                    false,
                    nodesToRead,
                    CancellationToken.None
                    ).ConfigureAwait(false);

                if (StatusCode.IsBad(response.Results[0].StatusCode))
                {
                    throw ServiceResultException.Create(response.Results[0].StatusCode.Code, "Reading OPC UA node failed!");
                }

                HistoryData values = ExtensionObject.ToEncodeable(response.Results[0].HistoryData) as HistoryData;
                foreach (DataValue value in values.DataValues)
                {
                    result += (value.ServerTimestamp.ToString() + '=' + value.ToString() + ',');
                }

                // read from the continuation points, if required
                while (response.Results[0].ContinuationPoint != null && response.Results[0].ContinuationPoint.Length > 0)
                {
                    nodeToRead.ContinuationPoint = response.Results[0].ContinuationPoint;

                    HistoryReadResponse continuedResponse = await session.HistoryReadAsync(
                        null,
                        new ExtensionObject(details),
                        TimestampsToReturn.Neither,
                        true,
                        nodesToRead,
                        CancellationToken.None).ConfigureAwait(false);

                    if (StatusCode.IsBad(continuedResponse.Results[0].StatusCode))
                    {
                        throw ServiceResultException.Create(continuedResponse.Results[0].StatusCode.Code, "Reading OPC UA node failed!");
                    }

                    values = ExtensionObject.ToEncodeable(continuedResponse.Results[0].HistoryData) as HistoryData;
                    foreach (DataValue value in values.DataValues)
                    {
                        result += (value.ServerTimestamp.ToString() + '=' + value.ToString() + ',');
                    }
                }

                return result.TrimEnd(',');
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Reading OPC UA node failed!");
                throw;
            }
            finally
            {
                if (session != null)
                {
                    if (session.Connected)
                    {
                        await session.CloseAsync().ConfigureAwait(false);
                    }

                    session.Dispose();
                }
            }
        }

        public async Task WriteUAVariableAsync(ApplicationConfiguration appConfiguration, string payload)
        {
            ISession session = null;

            try
            {
                using JsonDecoder decoder = new JsonDecoder(payload, new ServiceMessageContext(Program.Telemetry));

                string serverEndpoint = decoder.ReadString("Endpoint");
                session = await CreateSessionAsync(appConfiguration, serverEndpoint).ConfigureAwait(false);

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

                WriteResponse response = await session.WriteAsync(
                    null,
                    nodesToWrite,
                    CancellationToken.None).ConfigureAwait(false);

                ClientBase.ValidateResponse(results, nodesToWrite);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToWrite);

                if (StatusCode.IsBad(response.Results[0].Code))
                {
                    throw ServiceResultException.Create(response.Results[0].Code, 0, response.DiagnosticInfos, response.ResponseHeader.StringTable);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Writing OPC UA node failed!");
                throw;
            }
            finally
            {
                if (session != null)
                {
                    if (session.Connected)
                    {
                        await session.CloseAsync().ConfigureAwait(false);
                    }

                    session.Dispose();
                }
            }
        }

        private async Task<ISession> CreateSessionAsync(ApplicationConfiguration appConfiguration, string serverEndpoint)
        {
            if (string.IsNullOrEmpty(serverEndpoint))
            {
                Log.Logger.Error("Server endpoint is not specified!");
                throw new ArgumentException("Server endpoint is not specified!");
            }

            // find endpoint on a local OPC UA server
            EndpointDescription endpointDescription = await CoreClientUtils.SelectEndpointAsync(appConfiguration, serverEndpoint, true, Program.Telemetry).ConfigureAwait(false);
            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, EndpointConfiguration.Create());

            // check which identity to use
            UserIdentity userIdentity = new UserIdentity(new AnonymousIdentityToken());
            if ((Environment.GetEnvironmentVariable("UA_USERNAME") != null) && (Environment.GetEnvironmentVariable("UA_PASSWORD") != null))
            {
                userIdentity = new UserIdentity(Environment.GetEnvironmentVariable("UA_USERNAME"), Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("UA_PASSWORD")));
            }

            Log.Logger.Information("Creating secure session for endpoint {endpointUrl}.", endpoint.EndpointUrl);

            ISession session = await new DefaultSessionFactory(Program.Telemetry).CreateAsync(
                appConfiguration,
                endpoint,
                true,
                false,
                appConfiguration.ApplicationName,
                (uint)appConfiguration.ClientConfiguration.DefaultSessionTimeout,
                userIdentity,
                null)
                .ConfigureAwait(false);
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
