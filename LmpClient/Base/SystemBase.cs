using System.Threading.Tasks;
using LmpCommon.Message;

namespace LmpClient.Base
{
    /// <summary>
    /// Subsystem and system base class, it has a message factory to make message handling easier
    /// </summary>
    public abstract class SystemBase
    {
        /// <summary>
        /// Use this property to generate messages. Delegates to <see cref="LmpClient.Network.NetworkMain.CliMsgFactory"/>
        /// so the assembly-scan in <see cref="ClientMessageFactory"/>'s constructor runs only once per process
        /// instead of twice (previously this field and NetworkMain each constructed their own factory).
        /// </summary>
        public static ClientMessageFactory MessageFactory => LmpClient.Network.NetworkMain.CliMsgFactory;

        /// <summary>
        /// Main task factory, use it to instance new small tasks
        /// </summary>
        public static TaskFactory TaskFactory { get; } = new TaskFactory();

        /// <summary>
        /// Task factory to instance long running tasks
        /// </summary>
        public static TaskFactory LongRunTaskFactory { get; } = new TaskFactory(TaskCreationOptions.LongRunning, TaskContinuationOptions.None);
    }
}