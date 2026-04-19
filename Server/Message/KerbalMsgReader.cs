using System;
using LmpCommon.Message.Data.Kerbal;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using Server.Client;
using Server.Log;
using Server.Message.Base;
using Server.System;

namespace Server.Message
{
    public class KerbalMsgReader : ReaderBase
    {
        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var data = message.Data as KerbalBaseMsgData;
            switch (data?.KerbalMessageType)
            {
                case KerbalMessageType.Request:
                    KerbalSystem.HandleKerbalsRequest(client);
                    //We don't use this message anymore so we can recycle it
                    message.Recycle();
                    break;
                case KerbalMessageType.Proto:
                    KerbalSystem.HandleKerbalProto(client, (KerbalProtoMsgData)data);
                    break;
                case KerbalMessageType.Remove:
                    KerbalSystem.HandleKerbalRemove(client, (KerbalRemoveMsgData)data);

                    break;
                case KerbalMessageType.Reply:
                    // Server-originated; ignore if a client ever sends it (older protocol quirks).
                    return;
                default:
                    LunaLog.Debug($"Ignoring Kerbal message subtype {data?.KerbalMessageType} from {client.PlayerName}");
                    return;
            }
        }
    }
}
