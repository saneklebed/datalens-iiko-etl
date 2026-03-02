# RoomBroom — iikoChain Office plugin (пустые вкладки)

## Важно: кто что делает

- **Сборку ZIP делает тот, кто меняет код (в репо — AI).** После любого изменения кода плагина обязательно запускается `scripts/package.ps1`; на выходе — актуальный `dist/RoomBroomChainPlugin.zip`. Пользователю не предлагать «запусти скрипт» — пользователь не собирает.
- **Твоя задача (пользователь)** — взять готовый `RoomBroomChainPlugin.zip` из папки `dist`, перенести в папку плагинов (например `C:\Program Files\iiko\iikoChain\Office\Plugins`), распаковать и тестировать в Chain.

---

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

## Настройки (как сохраняются)

- Вкладка `Настройки` сохраняет значения в локальные настройки пользователя iikoChain (**`SettingsMain`**, JSON).
- Это нужно, чтобы вкладка `Документы` могла читать креды Диадока без повторного ввода.
- Реализация: `edo_iiko_bridge/RoomBroomChainPlugin/Config/ConfigStore.cs` + `Config/RoomBroomSettings.cs` + `Config/RoomBroomConfig.cs`.

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

## Как установить (только развертывание готового ZIP)

1) **Возьми готовый ZIP** из `dist/RoomBroomChainPlugin.zip` (его создаёт скрипт упаковки) и **распакуй** в:

`C:\Program Files\iiko\iikoChain\Office\Plugins`

2) Либо вручную: возьми `bin\Release\RoomBroomChainPlugin.dll` и положи в:

`C:\Program Files\iiko\iikoChain\Office\Plugins`

3) Перезапусти iikoChain Office — появится пункт `RoomBroom` с вкладками `Документы` и `Настройки`.

## Скачать ZIP через GitHub Actions (как “вчера”: запустил → скачал)

Ограничение: GitHub Actions **не может собрать DLL сам**, потому что для сборки нужны библиотеки iikoChain (`Resto.BackApi.Core*.dll`) из установленного iikoChain Office.

Поэтому делаем так:

1) Один раз собираешь DLL локально:
   - `dotnet build ... -c Release`
2) Убеждаешься, что в репо есть файл:
   - `edo_iiko_bridge/RoomBroomChainPlugin/bin/Release/RoomBroomChainPlugin.dll`
   (его можно закоммитить, если ок хранить бинарник в Git)
3) Запускаешь workflow **RoomBroom iikoChain plugin (ZIP)** в Actions.
4) Скачиваешь artifact `RoomBroomChainPlugin.zip` и распаковываешь в `...\Office\Plugins`.

