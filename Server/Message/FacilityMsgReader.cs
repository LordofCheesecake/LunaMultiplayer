using LmpCommon.Message.Data.Facility;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Server;
using LmpCommon.Message.Types;
using Server.Client;
using Server.Log;
using Server.Message.Base;
using Server.Server;
using Server.System.Scenario;

namespace Server.Message
{
    public class FacilityMsgReader : ReaderBase
    {
        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var data = message.Data as FacilityBaseMsgData;
            if (data == null)
            {
                LunaLog.Debug($"Facility message from {client.PlayerName} ignored: missing FacilityBaseMsgData payload");
                return;
            }

            switch (data.FacilityMessageType)
            {
                case FacilityMessageType.Repair:
                    LunaLog.Normal($"{client.PlayerName} REPAIRED facility {data.ObjectId}");
                    ScenarioDataUpdater.WriteRepairedDestroyedDataToFile(data.ObjectId, true);
                    break;
                case FacilityMessageType.Collapse:
                    LunaLog.Normal($"{client.PlayerName} DESTROYED facility {data.ObjectId}");
                    ScenarioDataUpdater.WriteRepairedDestroyedDataToFile(data.ObjectId, false);
                    break;
                default:
                    LunaLog.Debug($"Ignoring facility message subtype {data.FacilityMessageType} from {client.PlayerName}");
                    return;
            }

            //We don't do anything on the server side with this messages so just relay them.
            MessageQueuer.RelayMessage<FacilitySrvMsg>(client, data);
        }
    }
}
