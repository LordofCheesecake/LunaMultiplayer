using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Events;
using LmpCommon.Enums;
using LmpCommon.Locks;
using LmpCommon.Message.Data.Lock;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LmpClient.Systems.Lock
{
    public class LockMessageHandler : SubSystem<LockSystem>, IMessageHandler
    {
        public ConcurrentQueue<IServerMessageBase> IncomingMessages { get; set; } = new ConcurrentQueue<IServerMessageBase>();

        public void HandleMessage(IServerMessageBase msg)
        {
            if (!(msg.Data is LockBaseMsgData msgData)) return;

            switch (msgData.LockMessageType)
            {
                case LockMessageType.ListReply:
                    {
                        var data = (LockListReplyMsgData)msgData;

                        // ListReply is an authoritative snapshot. Clear the local store first so stale entries
                        // from a previous session / reconnect can't survive the refresh.
                        LockSystem.LockStore.ClearAllLocks();

                        for (var i = 0; i < data.LocksCount; i++)
                        {
                            LockSystem.LockStore.AddOrUpdateLock(data.Locks[i]);
                        }

                        if (MainSystem.NetworkState < ClientState.LocksSynced)
                            MainSystem.NetworkState = ClientState.LocksSynced;
                    }
                    break;
                case LockMessageType.Acquire:
                    {
                        var data = (LockAcquireMsgData)msgData;
                        LockSystem.LockStore.AddOrUpdateLock(data.Lock);

                        LockEvent.onLockAcquire.Fire(data.Lock);
                    }
                    break;
                case LockMessageType.Release:
                    {
                        var data = (LockReleaseMsgData)msgData;
                        LockSystem.LockStore.RemoveLock(data.Lock);

                        LockEvent.onLockRelease.Fire(data.Lock);
                    }
                    break;
            }
        }
    }
}
