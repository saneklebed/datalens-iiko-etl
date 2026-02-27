# RoomBroom — iikoChain Office plugin (пустые вкладки)

Цель: получить в iikoChain Office **пункт в левой навигации** `RoomBroom` с двумя вкладками:

- `Документы` — пустая страница
- `Настройки` — пустая страница

Плагин повторяет каркас EDI‑Doc:

- `RBPlugin : INavBarPlugin, IPlugin` → `MenuGroup` → `MenuItem(TabPageFirst)` + `MenuItem(TabPageSecond)`
- `TabPageFirst/Second : PluginTabPageBase` → `CreateControl()` возвращает `UserControl`

## Как собрать

1) Скопируй `Directory.Build.props.example` → `Directory.Build.props`
2) В `Directory.Build.props` укажи `IikoChainLibDir` — путь к папке iikoChain Office, где лежат:
   - `Resto.BackApi.Core.dll`
   - `Resto.BackApi.Core.Plugin.dll`

Пример:

`C:\Users\Orange\Desktop\iiko\iikoChain\Chain Office 8.5.8002.0\`

3) Собери проект:

```powershell
dotnet build edo_iiko_bridge\RoomBroomChainPlugin\RoomBroomChainPlugin.csproj -c Release
```

## Собрать ZIP “распаковать и работает”

Запусти (PowerShell):

```powershell
.\edo_iiko_bridge\RoomBroomChainPlugin\scripts\package.ps1
```

Если iikoChain Office установлен в `C:\Program Files\iiko\iikoChain\Office\`, то `Directory.Build.props` можно не создавать и собрать так:

```powershell
.\edo_iiko_bridge\RoomBroomChainPlugin\scripts\package.ps1 -IikoChainLibDir "C:\Program Files\iiko\iikoChain\Office"
```

На выходе будет `edo_iiko_bridge\RoomBroomChainPlugin\dist\RoomBroomChainPlugin.zip`.

## Как установить

1) **Распакуй ZIP** в:

`C:\Program Files\iiko\iikoChain\Office\Plugins`

2) Либо вручную: возьми `bin\Release\RoomBroomChainPlugin.dll` и положи в:

`C:\Program Files\iiko\iikoChain\Office\Plugins`

3) Перезапусти iikoChain Office — появится пункт `RoomBroom` с вкладками `Документы` и `Настройки`.

