using LmpCommon.Message.Data.Screenshot;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using Server.Client;
using Server.Log;
using Server.Message.Base;
using Server.System;

namespace Server.Message
{
    public class ScreenshotMsgReader : ReaderBase
    {
        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var data = message.Data as ScreenshotBaseMsgData;
            if (data == null)
            {
                LunaLog.Debug($"Screenshot message from {client.PlayerName} ignored: missing ScreenshotBaseMsgData payload");
                return;
            }

            switch (data.ScreenshotMessageType)
            {
                case ScreenshotMessageType.FoldersRequest:
                    ScreenshotSystem.SendScreenshotFolders(client);
                    break;
                case ScreenshotMessageType.ListRequest:
                    ScreenshotSystem.SendScreenshotList(client, (ScreenshotListRequestMsgData)data);
                    break;
                case ScreenshotMessageType.ScreenshotData:
                    ScreenshotSystem.SaveScreenshot(client, (ScreenshotDataMsgData)data);
                    break;
                case ScreenshotMessageType.DownloadRequest:
                    ScreenshotSystem.SendScreenshot(client, (ScreenshotDownloadRequestMsgData)data);
                    break;
                default:
                    LunaLog.Debug($"Ignoring screenshot message subtype {data.ScreenshotMessageType} from {client.PlayerName}");
                    break;
            }
        }
    }
}
