
namespace Opc.Ua.Cloud.Commander
{
    using Opc.Ua;
    using Opc.Ua.Server;
    using Serilog;
    using System;
    using System.Text;

    public partial class UAServer : StandardServer
    {
        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties
            {
                ManufacturerName = "OPC Foundation",
                ProductName = "UA Cloud Commander",
                ProductUri = "",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp()
            };

            return properties;
        }

        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            server.SessionManager.ImpersonateUser += new ImpersonateEventHandler(SessionManager_ImpersonateUser);
        }

        private void SessionManager_ImpersonateUser(ISession session, ImpersonateEventArgs args)
        {
            UserNameIdentityToken userNameToken = args.NewIdentity as UserNameIdentityToken;
            if (userNameToken != null)
            {
                args.Identity = VerifyPassword(userNameToken);

                Log.Logger.Information("Username token accepted: {0}", args.Identity?.DisplayName);
                return;
            }

            AnonymousIdentityToken anonymousToken = args.NewIdentity as AnonymousIdentityToken;
            if (anonymousToken != null)
            {
                // Assign an identity object for anonymous sessions as well. Without this
                // args.Identity stays null, so the session has no IUserIdentity at all and
                // any privileged call made on it fails with BadUserAccessDenied before the
                // role check can even run (see ConfigurationNodeManager.
                // HasApplicationSecureAdminAccess). This matters for GDS Server Push: the
                // push client connects anonymously and only elevates per operation. The
                // anonymous identity deliberately carries no roles - privileged operations
                // still require the username token, which VerifyPassword maps to
                // SystemConfigurationIdentity (SecurityAdmin).
                args.Identity = new UserIdentity(anonymousToken);

                Log.Logger.Information("Anonymous token accepted: {0}", args.Identity?.DisplayName);
                return;
            }

            throw ServiceResultException.Create(StatusCodes.BadIdentityTokenInvalid, "Not supported user token type: {0}.", args.NewIdentity);
        }

        private IUserIdentity VerifyPassword(UserNameIdentityToken userNameToken)
        {
            var userName = userNameToken.UserName;
            var password = Encoding.UTF8.GetString(userNameToken.DecryptedPassword);

            if (string.IsNullOrEmpty(userName))
            {
                throw ServiceResultException.Create(StatusCodes.BadIdentityTokenInvalid,
                    "Security token is not a valid username token. An empty username is not accepted.");
            }

            if (string.IsNullOrEmpty(password))
            {
                throw ServiceResultException.Create(StatusCodes.BadIdentityTokenRejected,
                    "Security token is not a valid username token. An empty password is not accepted.");
            }

            string configuredUsername = Environment.GetEnvironmentVariable("OPCUA_USERNAME");
            string configuredPassword = Environment.GetEnvironmentVariable("OPCUA_PASSWORD");

            if (!string.IsNullOrEmpty(configuredUsername)
             && !string.IsNullOrEmpty(configuredPassword)
             && (userName == configuredUsername)
             && (password == configuredPassword))
            {
                return new SystemConfigurationIdentity(new UserIdentity(userNameToken));
            }

            // create an exception with a vendor defined sub-code.
            throw new ServiceResultException(new StatusCode(
                StatusCodes.BadUserAccessDenied,
                "InvalidPassword"));
        }
    }
}
