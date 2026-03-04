using System;
using Resto.BackApi.Core;
using Resto.BackApi.Core.Plugin;
using RoomBroomChainPlugin;

public class RBPlugin : INavBarPlugin, IPlugin
{
    private const string Version = "0.1";
    private readonly string MenuName = "RoomBroom";
    private readonly RBPluginCore _core = new RBPluginCore();

    public MenuGroup MenuGroup
    {
        get
        {
            return new MenuGroup(
                MenuName,
                new BaseMenuItem[]
                {
                    new MenuItem(new TabPageFirst(), MenuName),
                    new MenuItem(new TabPageSecond(), MenuName),
                },
                MenuName
            );
        }
    }

    /// <summary>
    /// Вызывается хостом iikoChain с типом IPluginAdapter. Оборачиваем в наш RBPluginAdapter и передаём в ядро.
    /// </summary>
    public void SetAdapter(IPluginAdapter adapter)
    {
        _core.SetAdapter(new RBPluginAdapterWrapper(adapter));
    }

    /// <summary>
    /// Внутренняя логика плагина; работает только с нашим типом RBPluginAdapter.
    /// </summary>
    private sealed class RBPluginCore
    {
        public void SetAdapter(RBPluginAdapter adapter)
        {
            // Пока ничего не делаем. Каркас нужен только для появления пункта меню в iikoChain.
        }
    }
}
