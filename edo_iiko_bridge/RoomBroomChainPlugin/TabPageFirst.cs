using System.Windows.Forms;
using Resto.BackApi.Core;

public class TabPageFirst : PluginTabPageBase
{
    private static readonly string Name = "Документы";

    public TabPageFirst() : base(Name)
    {
    }

    public override UserControl CreateControl()
    {
        return new Pages.DocsPage();
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

