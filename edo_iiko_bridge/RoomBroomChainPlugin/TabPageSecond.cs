using System.Windows.Forms;
using Resto.BackApi.Core;

public class TabPageSecond : PluginTabPageBase
{
    private static readonly string Name = "Настройки";

    public TabPageSecond() : base(Name)
    {
    }

    public override UserControl CreateControl()
    {
        return new Pages.SettingsPage();
    }

    public override bool LoadData(UserControl control)
    {
        return true;
    }

    public override string GetTabPageId()
    {
        return Name;
    }

    public override bool Closed(UserControl control)
    {
        return base.Closed(control);
    }
}

