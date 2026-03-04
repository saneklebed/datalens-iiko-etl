using Resto.BackApi.Core.Plugin;

namespace RoomBroomChainPlugin
{
    /// <summary>
    /// Наш тип адаптера хоста iikoChain. Хост передаёт <see cref="IPluginAdapter"/>,
    /// мы оборачиваем его в <see cref="RBPluginAdapterWrapper"/> и дальше работаем только с RBPluginAdapter.
    /// </summary>
    public interface RBPluginAdapter
    {
    }

    /// <summary>
    /// Обёртка над IPluginAdapter хоста. Реализует наш интерфейс RBPluginAdapter.
    /// </summary>
    public sealed class RBPluginAdapterWrapper : RBPluginAdapter
    {
        private readonly IPluginAdapter _inner;

        public RBPluginAdapterWrapper(IPluginAdapter inner)
        {
            _inner = inner;
        }
    }
}
