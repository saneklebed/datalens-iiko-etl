# RoomBroom — iikoChain Office plugin (ЭДО-мост Diadoc)

## Важно: кто что делает

- **Сборку ZIP делает AI — всегда сам, одной командой.** После любого изменения кода плагина AI запускает `scripts/package.ps1` (один вызов powershell -File ...) и получает `dist/RoomBroomChainPlugin.zip`. Пользователю не предлагать «запусти скрипт»; упаковку не делать вручную по шагам.
- **Задача пользователя** — взять готовый `RoomBroomChainPlugin.zip` из `dist`, перенести в папку плагинов iikoChain, распаковать и тестировать.

---

## Что делает плагин

Плагин добавляет в iikoChain Office пункт **RoomBroom** с двумя вкладками:

### Настройки
- Логин, Пароль, API-токен Diadoc
- Тогл создания накладных
- Данные хранятся в локальных настройках iikoChain (SettingsMain, JSON)

### Документы
- **Комбобокс юрлица** — загружается из Diadoc API (GetMyOrganizations)
- **Три режима:**
  - **Черновики** — структура есть, функционал минимальный
  - **Контрагенты** — таблица Организация/ИНН/КПП. Пагинация (все контрагенты, не только 100). Сокращённые названия
  - **Входящие** — фильтр по дате (DateEdit «С»/«По»), кнопка «Получить накладные». Колонки: Отправитель, ИНН, Номер, От, Отправлен в ЭДО, Сумма НДС, Сумма, Статус, Поставщик, Накладная ЭДО

**Сопоставление документов с контрагентами:**
- Отправитель/ИНН определяются через список контрагентов: сначала по CounteragentBoxId, затем по SellerInn (из Metadata)
- Контрагенты кэшируются, сбрасываются при смене юрлица

**Цветовая раскраска:**
- Подписан → зелёный, Отказ/Отклонён → розовый
- Поставщик найден → зелёный
- Накладная заполнена → зелёный

## Структура проекта

```
RoomBroomChainPlugin/
├── Config/
│   ├── ConfigStore.cs          # Чтение/запись настроек iikoChain
│   ├── RoomBroomConfig.cs      # Модель настроек
│   └── RoomBroomSettings.cs    # Ключи настроек
├── Diadoc/
│   └── DiadocApiClient.cs      # HTTP-клиент Diadoc API (HttpWebRequest)
├── Pages/
│   ├── DocsPage.cs             # UI вкладки «Документы»
│   └── SettingsPage.cs         # UI вкладки «Настройки»
├── scripts/
│   └── package.ps1             # Сборка + ZIP
└── dist/
    └── RoomBroomChainPlugin.zip  # Готовый архив для развёртывания
```

## Как собрать

1) Скопируй `Directory.Build.props.example` → `Directory.Build.props`
2) Укажи `IikoChainLibDir` — путь к iikoChain Office (где лежат `Resto.BackApi.Core*.dll`)
3) Собери:

```powershell
.\edo_iiko_bridge\RoomBroomChainPlugin\scripts\package.ps1
```

На выходе: `dist/RoomBroomChainPlugin.zip`

## Как установить

1) Возьми `dist/RoomBroomChainPlugin.zip`
2) Распакуй в `C:\Program Files\iiko\iikoChain\Office\Plugins`
3) Перезапусти iikoChain Office — появится пункт «RoomBroom»

## Технические заметки

- DevExpress **v16.2** (из iikoChain), а не v21.2
- HttpWebRequest (не HttpClient) — аналог SDK Diadoc, работает в среде iikoChain
- BoxId из API: убирать суффикс `@diadoc.ru` через `StripBoxIdDomain`
- DateEdit: НЕ добавлять `Buttons.AddRange` (дефолт содержит Combo-кнопку). LabelControl рядом — задавать явный `Size`
- Контрагенты: пагинация обязательна (afterIndexKey), иначе только первые 100
