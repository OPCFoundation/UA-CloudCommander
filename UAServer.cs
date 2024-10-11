
namespace Opc.Ua.Cloud.Commander
{
    using Opc.Ua;
    using Opc.Ua.Server;
    using System;

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

        private void SessionManager_ImpersonateUser(Session session, ImpersonateEventArgs args)
        {
            UserNameIdentityToken userNameToken = args.NewIdentity as UserNameIdentityToken;
            if (userNameToken != null)
            {
                args.Identity = VerifyPassword(userNameToken);

                Utils.LogInfo(Utils.TraceMasks.Security, "Username token accepted: {0}", args.Identity?.DisplayName);
                return;
            }

            AnonymousIdentityToken anonymousToken = args.NewIdentity as AnonymousIdentityToken;
            if (anonymousToken != null)
            {
                Utils.LogInfo(Utils.TraceMasks.Security, "Anonymous token accepted: {0}", args.Identity?.DisplayName);
                return;
            }

            throw ServiceResultException.Create(StatusCodes.BadIdentityTokenInvalid, "Not supported user token type: {0}.", args.NewIdentity);
        }

        private IUserIdentity VerifyPassword(UserNameIdentityToken userNameToken)
        {
            var userName = userNameToken.UserName;
            var password = userNameToken.DecryptedPassword;

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

            // construct translation object with default text.
            TranslationInfo info = new TranslationInfo(
                "InvalidPassword",
                "en-US",
                "Invalid username or password.",
                userName);

            // create an exception with a vendor defined sub-code.
            throw new ServiceResultException(new ServiceResult(
                StatusCodes.BadUserAccessDenied,
                "InvalidPassword",
                LoadServerProperties().ProductUri,
                new LocalizedText(info)));
        }
    }
}
