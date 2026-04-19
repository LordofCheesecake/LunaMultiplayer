using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Data.Vessel;
using LmpCommon.Message.Server.Base;
using LmpCommon.Message.Types;
using System;
using System.Collections.Generic;

namespace LmpCommon.Message.Server
{
    public class VesselSrvMsg : SrvMsgBase<VesselBaseMsgData>
    {
        /// <inheritdoc />
        internal VesselSrvMsg() { }

        /// <inheritdoc />
        public override string ClassName { get; } = nameof(VesselSrvMsg);

        /// <inheritdoc />
        protected override Dictionary<ushort, Type> SubTypeDictionary { get; } = new Dictionary<ushort, Type>
        {
            [(ushort)VesselMessageType.Proto] = typeof(VesselProtoMsgData),
            [(ushort)VesselMessageType.Remove] = typeof(VesselRemoveMsgData),
            [(ushort)VesselMessageType.Position] = typeof(VesselPositionMsgData),
            [(ushort)VesselMessageType.Flightstate] = typeof(VesselFlightStateMsgData),
            [(ushort)VesselMessageType.Update] = typeof(VesselUpdateMsgData),
            [(ushort)VesselMessageType.Resource] = typeof(VesselResourceMsgData),
            [(ushort)VesselMessageType.PartSyncField] = typeof(VesselPartSyncFieldMsgData),
            [(ushort)VesselMessageType.PartSyncUiField] = typeof(VesselPartSyncUiFieldMsgData),
            [(ushort)VesselMessageType.PartSyncCall] = typeof(VesselPartSyncCallMsgData),
            [(ushort)VesselMessageType.ActionGroup] = typeof(VesselActionGroupMsgData),
            [(ushort)VesselMessageType.Fairing] = typeof(VesselFairingMsgData),
            [(ushort)VesselMessageType.Decouple] = typeof(VesselDecoupleMsgData),
            [(ushort)VesselMessageType.Couple] = typeof(VesselCoupleMsgData),
            [(ushort)VesselMessageType.Undock] = typeof(VesselUndockMsgData),
        };

        public override ServerMessageType MessageType => ServerMessageType.Vessel;

        /// <summary>
        /// Channel assignment. ReliableOrdered is per-channel: within a channel a large message (e.g. a full
        /// vessel Proto) head-of-line-blocks all smaller messages queued after it. To avoid that, Proto gets
        /// its own channel so fast-changing reliable updates (ActionGroup, PartSync, Couple/Undock) don't get
        /// stuck behind a multi-KB Proto serialize/send.
        /// Unreliable subtypes are forced to channel 0 by Lidgren - no split possible there.
        /// </summary>
        protected override int DefaultChannel
        {
            get
            {
                if (IsUnreliableMessage()) return 0;
                if (Data.SubType == (ushort)VesselMessageType.Proto) return 9;
                return 8;
            }
        }

        public override NetDeliveryMethod NetDeliveryMethod => IsUnreliableMessage() ?
            NetDeliveryMethod.UnreliableSequenced : NetDeliveryMethod.ReliableOrdered;

        private bool IsUnreliableMessage()
        {
            return Data.SubType == (ushort)VesselMessageType.Position || Data.SubType == (ushort)VesselMessageType.Flightstate
                   || Data.SubType == (ushort)VesselMessageType.Update || Data.SubType == (ushort)VesselMessageType.Resource;
        }
    }
}