using LmpCommon.Message.Data.Handshake;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using Server.Client;
using Server.Log;
using Server.Message.Base;
using Server.System;

namespace Server.Message
{
    public class HandshakeMsgReader : ReaderBase
    {
        private static readonly HandshakeSystem HandshakeHandler = new HandshakeSystem();

        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var data = message.Data as HandshakeBaseMsgData;
            switch (data?.HandshakeMessageType)
            {
                case HandshakeMessageType.Request:
                    HandshakeHandler.HandleHandshakeRequest(client, (HandshakeRequestMsgData)data);
                    break;
                default:
                    LunaLog.Debug($"Ignoring handshake subtype {data?.HandshakeMessageType} from {client.PlayerName}");
                    break;
            }
        }
    }
}
