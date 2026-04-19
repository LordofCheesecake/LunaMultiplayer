using System;
using LmpCommon.Message.Data.Lock;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using Server.Client;
using Server.Log;
using Server.Message.Base;
using Server.System;

namespace Server.Message
{
    public class LockSystemMsgReader : ReaderBase
    {
        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var data = message.Data as LockBaseMsgData;
            if (data == null)
            {
                LunaLog.Debug("Lock message ignored: missing or invalid LockBaseMsgData payload");
                return;
            }

            switch (data.LockMessageType)
            {
                case LockMessageType.ListRequest:
                    LockSystemSender.SendAllLocks(client);
                    break;
                case LockMessageType.Acquire:
                    var acquireData = (LockAcquireMsgData)data;
                    if (acquireData.Lock.PlayerName == client.PlayerName)
                        LockSystemSender.SendLockAcquireMessage(client, acquireData.Lock, acquireData.Force);
                    break;
                case LockMessageType.Release:
                    var releaseData = (LockReleaseMsgData)data;
                    if (releaseData.Lock.PlayerName == client.PlayerName)
                        LockSystemSender.ReleaseAndSendLockReleaseMessage(client, releaseData.Lock);
                    break;
                default:
                    LunaLog.Debug($"Ignoring lock message subtype {data.LockMessageType} from {client.PlayerName}");
                    return;
            }
        }
    }
}
