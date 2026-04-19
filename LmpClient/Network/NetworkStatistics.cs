using LmpCommon.Message.Base;

namespace LmpClient.Network
{
    public class NetworkStatistics
    {
        public static volatile float PingSec;
        public static float AvgPingSec => NetworkMain.ClientConnection?.ServerConnection?.AverageRoundtripTime ?? 0f;
        public static int SentBytes => NetworkMain.ClientConnection?.Statistics?.SentBytes ?? 0;
        public static int ReceivedBytes => NetworkMain.ClientConnection?.Statistics?.ReceivedBytes ?? 0;
        public static float TimeOffset => NetworkMain.ClientConnection?.ServerConnection?.RemoteTimeOffset ?? 0f;
        public static int MessagesInCache => MessageStore.GetMessageCount(null);
        public static int MessageDataInCache => MessageStore.GetMessageDataCount(null);
    }
}
