using Lidgren.Network;

namespace LmpCommon.Message.Interface
{
    public interface IMessageBase
    {
        /// <summary>
        /// Name of the class
        /// </summary>
        string ClassName { get; }

        /// <summary>
        /// Class with the data that it handles
        /// </summary>
        IMessageData Data { get; }

        /// <summary>
        /// True if the data version property mismatches
        /// </summary>
        bool VersionMismatch { get; set; }

        /// <summary>
        /// Specify how the message should be delivered based on lidgren library.
        /// This is important to avoid lag!
        /// Unreliable: No guarantees. (Use for unimportant messages like heartbeats)
        /// UnreliableSequenced: Late messages will be dropped if newer ones were already received.
        /// ReliableUnordered: All packages will arrive, but not necessarily in the same order.
        /// ReliableSequenced: All packages will arrive, but late ones will be dropped.
        /// This means that we will always receive the latest message eventually, but may miss older ones.
        /// ReliableOrdered: All packages will arrive, and they will do so in the same order.
        /// Unlike all the other methods, here the library will hold back messages until all previous ones are received,
        /// before handing them to us.
        /// </summary>
        NetDeliveryMethod NetDeliveryMethod { get; }

        /// <summary>
        /// Public accessor to retrieve the Channel correctly
        /// </summary>
        /// <returns></returns>
        int Channel { get; }

        /// <summary>
        /// Attaches the data to the message
        /// </summary>
        void SetData(IMessageData data);

        /// <summary>
        /// Retrieves a message data from the pool based on the subtype
        /// </summary>
        IMessageData GetMessageData(ushort subType);

        /// <summary>
        /// This method retrieves the message as a byte array with it's 8 byte header at the beginning
        /// </summary>
        /// <param name="lidgrenMsg">Lidgren message to serialize to</param>
        /// <returns>Mesage as a byte array with it's header</returns>
        void Serialize(NetOutgoingMessage lidgrenMsg);

        /// <summary>
        /// Call this method to send the message back to the pool (both the wrapper and the attached Data).
        /// Safe to call multiple times (idempotent after the first call).
        /// </summary>
        void Recycle();

        /// <summary>
        /// Recycle only the wrapper, leaving <see cref="Data"/> alone so that other wrappers still referencing
        /// the same shared <see cref="IMessageData"/> (e.g. after a broadcast relay) can continue to serialize it
        /// without use-after-free. Use this from the central receive-finally path when you do not own the Data.
        /// </summary>
        void RecycleWrapperOnly();

        /// <summary>
        /// Gets the message size in bytes
        /// </summary>
        int GetMessageSize();
    }
}