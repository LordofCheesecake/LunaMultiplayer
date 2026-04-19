using System;
using System.Collections.Concurrent;
using LmpCommon.Enums;
using LmpCommon.Message.Data.Admin;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Server;
using LmpCommon.Message.Types;
using Server.Client;
using Server.Command;
using Server.Context;
using Server.Log;
using Server.Message.Base;
using Server.Server;
using Server.Settings.Structures;

namespace Server.Message
{
    public class AdminMsgReader : ReaderBase
    {
        /// <summary>Tracks failed admin-auth attempts per PlayerName so repeated guesses trigger a lockout.</summary>
        private static readonly ConcurrentDictionary<string, AdminAuthState> FailureTracker = new ConcurrentDictionary<string, AdminAuthState>();

        /// <summary>How many consecutive bad-password attempts tolerated before the sender is locked out.</summary>
        private const int MaxFailedAttempts = 5;

        /// <summary>Lockout duration after exceeding <see cref="MaxFailedAttempts"/>.</summary>
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

        private sealed class AdminAuthState
        {
            public int FailedAttempts;
            public DateTime LockoutUntilUtc;
        }

        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var messageData = (AdminBaseMsgData)message.Data;
            var auth = FailureTracker.GetOrAdd(client.PlayerName ?? string.Empty, _ => new AdminAuthState());

            if (auth.LockoutUntilUtc > DateTime.UtcNow)
            {
                LunaLog.Warning($"{client.PlayerName}: admin command rejected (locked out for {(auth.LockoutUntilUtc - DateTime.UtcNow).TotalSeconds:F0}s more)");
                var deniedMsg = ServerContext.ServerMessageFactory.CreateNewMessageData<AdminReplyMsgData>();
                deniedMsg.Response = AdminResponse.InvalidPassword;
                MessageQueuer.SendToClient<AdminSrvMsg>(client, deniedMsg);
                return;
            }

            if (!string.IsNullOrEmpty(GeneralSettings.SettingsStore.AdminPassword) && GeneralSettings.SettingsStore.AdminPassword == messageData.AdminPassword)
            {
                auth.FailedAttempts = 0;
                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<AdminReplyMsgData>();
                switch (messageData.AdminMessageType)
                {
                    case AdminMessageType.Ban:
                        var banMsg = (AdminBanMsgData)message.Data;
                        LunaLog.Debug($"{client.PlayerName}: Requested a ban against {banMsg.PlayerName}. Reason: {banMsg.Reason}");
                        msgData.Response = CommandHandler.Commands["ban"].Func($"{banMsg.PlayerName} {banMsg.Reason}") ? AdminResponse.Ok : AdminResponse.Error;
                        break;
                    case AdminMessageType.Kick:
                        var kickMsg = (AdminKickMsgData)message.Data;
                        LunaLog.Debug($"{client.PlayerName}: Requested a kick against {kickMsg.PlayerName}. Reason: {kickMsg.Reason}");
                        msgData.Response = CommandHandler.Commands["kick"].Func($"{kickMsg.PlayerName} {kickMsg.Reason}") ? AdminResponse.Ok : AdminResponse.Error;
                        break;
                    case AdminMessageType.Dekessler:
                        LunaLog.Debug($"{client.PlayerName}: Requested a dekessler");
                        CommandHandler.Commands["dekessler"].Func(null);
                        msgData.Response = AdminResponse.Ok;
                        break;
                    case AdminMessageType.Nuke:
                        LunaLog.Debug($"{client.PlayerName}: Requested a nuke");
                        CommandHandler.Commands["nukeksc"].Func(null);
                        msgData.Response = AdminResponse.Ok;
                        break;
                    case AdminMessageType.RestartServer:
                        LunaLog.Debug($"{client.PlayerName}: Requested a server restart");
                        CommandHandler.Commands["restartserver"].Func(null);
                        msgData.Response = AdminResponse.Ok;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                MessageQueuer.SendToClient<AdminSrvMsg>(client, msgData);
            }
            else
            {
                auth.FailedAttempts++;
                if (auth.FailedAttempts >= MaxFailedAttempts)
                {
                    auth.LockoutUntilUtc = DateTime.UtcNow + LockoutDuration;
                    LunaLog.Warning($"{client.PlayerName}: locked out from admin commands for {LockoutDuration.TotalMinutes:F0} min after {auth.FailedAttempts} failed attempts");
                }
                else
                {
                    LunaLog.Warning($"{client.PlayerName}: Tried to run an admin command with an invalid password (attempt {auth.FailedAttempts}/{MaxFailedAttempts})");
                }

                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<AdminReplyMsgData>();
                msgData.Response = AdminResponse.InvalidPassword;
                MessageQueuer.SendToClient<AdminSrvMsg>(client, msgData);
            }
        }
    }
}
