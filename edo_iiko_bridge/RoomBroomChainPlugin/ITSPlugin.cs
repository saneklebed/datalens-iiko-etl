using System;
using Resto.BackApi.Core;
using Resto.BackApi.Core.Plugin;

public class RBPlugin : INavBarPlugin, IPlugin
{
    private const string Version = "0.1";
    private readonly string MenuName = "RoomBroom";

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

    public void SetAdapter(IPluginAdapter adapter)
    {
        // Пока ничего не делаем. Каркас нужен только для появления пункта меню в iikoChain.
    }
}

