# Контекст для AI при смене устройства

**Назначение:** сюда фиксируем изменения по дням (решения, что сделано, где что лежит), чтобы при открытии проекта с другого ПК (домашний sanek / рабочий Orange) AI имел актуальный контекст без потери нити.

**Для AI:** при первом обращении в новой сессии или при вопросе «что делали в последнее время» — прочитать последние блоки по датам ниже. Полные дневные отчёты — в файлах `YYYY-MM-DD.md` в этой же папке.

---

## 16.02.2025 — мост ЭДО ↔ iiko + интеграция с Kvant API (рабочий ПК)

**Мост ЭДО ↔ iiko** (`edo_iiko_bridge/`):
- Диадок: DiadocClient (auth, GetMyOrganizations, GetDocuments, GetEntityContent). CLI: fetch-incoming, fetch-document &lt;messageId&gt; &lt;entityId&gt;.
- УПД: парсер XML (артикул, единица измерения, название, кол-во, цена, сумма). Парсер в `parsers/upd.py`.
- iiko Resto: IikoRestoClient (auth как в ETL), get_products(). CLI: list-products.
- Маппинг: mapping_store (load/save, find_mapping_for_line). Workflow `edo-iiko-bridge.yml`: тесты при push/PR, ручной запуск — test + fetch-incoming. Документация: `docs/edo-iiko-iiko-edi-api.md`.

**Kvant API** (`alerts_bot.py` + `docs/kvant-integration.md`):
- Подключение к Кванту по API: `POST https://platform.kvant.app/openapi/tasks/store`, заголовок `api-key`. Секреты: KVANT_API_KEY, KVANT_ASSIGNEE_ID (в `alerts.yml`).
- После рассылки отчёта в Telegram создаются **три задачи в Кванте**: 1) коммуникация «Ознакомиться с результатами инвент» (текст отчёта); 2) задача «ТОП недостач» (ТОП-2 по филиалам, проверка приходов, инструкция по ТК/ежедневному инвенту, срок 5 дней); 3) задача «Результаты по ТОП недостач» (сравнение недель, фиксация прогресса, срок 24 ч).

**Что делать с домашнего ПК:** вытянуть репо, дописать `reports/2025-02-16.md` блоком «С домашнего ПК». **Визуал в iiko:** воспроизвести интерфейс (папки Plugins и т.д.), пункт в меню, на который можно тыкать — точка входа для моста ЭДО ↔ iiko.

---

## 24.02.2026

**Neon / миграции:**
- Пересорт: добавлена пара товаров Говядина мякоть ↔ Говядина лопатка (для персонала) — таблица `inventory_core.resort_product_pairs`, миграция `add-resort-pair-beef.sql`. Порог пересорта во view `weekly_possible_resort_products` изменён с 10% на 25% (`resort-threshold-25pct.sql`). Логика пересорта живёт в витрине **inventory_mart.weekly_deviation_products_money_v2** (поле `is_possible_resort`), не в core; миграция add-resort-product-name-pairs откачена (`revert-resort-product-name-pairs.sql`).
- Порча и списания: движение (оборот) считается **без** Порчи (фильтр в `inventory_core.transactions`: `contr_account_name <> 'Порча'`). Таблица «Списания» в дашборде показывает в т.ч. Порчу — view `weekly_product_documents_products` переведён на чтение из **olap_postings** (`weekly_product_documents_include_spoilage.sql`).

**DataLens:**
- Один период на дашборд: фильтр «Выбор диапазона для анализа», в таблицах период в формате H5. Четыре ТОП-таблицы на вкладке «Результаты»: отриц./положит. в % и в $; общий селектор «ТОП N». Для каждой таблицы — поле ранга и фильтр `[поле_ранга] <= INT([top_n_param])`. Добавлены ранги и фильтры для топа по положительным деньгам (`rank_by_surplus_money`, `deviation_money_signed`) и по положительным % (`rank_by_surplus_pct`, `fact_deviation_pct_qty`). Формулы для всех четырёх — в `.cursor/rules/datalens-iiko-knowledge.md`, раздел «ТОП N по отклонениям».

**Документация:**
- project.mdc и README обновлены: пересорт (источник is_possible_resort в mart), порча/списания, четыре ТОП-таблицы, ТОП N, контекст с двух ПК. В базу знаний добавлен раздел с формулами рангов и фильтров ТОП N.

---

## Текущая сессия (рабочий ПК → продолжить с домашнего)

**Telegram-бот с алармами (`alerts_bot.py` + GitHub Actions):**
- Запуск через workflow **Inventory alerts to Telegram** (`.github/workflows/alerts.yml`). Секреты: `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID` (можно с минусом для группы; при миграции группы в супергруппу — обновить на новый id, например `-1003540235437`). `ALERTS_TOP_N` опционально, по умолчанию 5.
- Режимы: `ALERTS_MODE=once` (по умолчанию в Actions) — один раз собрать отчёт и отправить; `ALERTS_MODE=bot` — бот с `/start` и `/week`. В режиме once отправка асинхронная: `asyncio.run(send())`, иначе сообщения не уходят. Обработка `ChatMigrated`: при переезде группы в супергруппу повторяем отправку с `e.new_chat_id` и выводим подсказку обновить секрет.
- Отчёт разбит по филиалам: **по одному сообщению на филиал**, затем **одно сводное** (сумма недостач + излишков по филиалам, `sum(deviation_money_signed)` по департаменту). Запросы к Neon без изменений — только формат вывода.

**Формат отчёта в боте:**
- ТОП по 5 позиций (без артикулов), разделитель везде **` | `**.
- По каждому филиалу блоки: Несохранённые позиции; Неверно посчитанные позиции неделю назад; **Позиции с пересортом** (из `is_possible_resort`); ТОП недостач в деньгах; ТОП излишков в деньгах; ТОП недостач в %; ТОП излишков в %. В ТОПах (деньги и %) **исключены позиции с пересортом** (`is_possible_resort is null or false` в SQL).
- Строки ТОПов: `товар | отклонение | норма | превышение нормы` (для денег — `allowed_loss_money` как норма, для % — `norm_pct`). Отклонение со знаком (минус для недостач). Проценты: и отклонение, и превышение нормы в % (не п.п.).
- Неделя в подписи: **фактическая неделя** — конец периода показываем как `week_end - 1` день (`week_end_to_display_end()`), чтобы было «17 — 23», а не «17 — 24».

**DataLens (из прошлых сессий):**
- На вкладке «Результаты» фильтр по «чистым» позициям: в MONEY — `is_clean_position_money` (флаги + `show_row_by_unit_price`), в QTY — `is_clean_position_pct` (движение > 1). В витрину QTY добавлено поле `is_missing_inventory_position` (миграция `add-is-missing-inventory-position-to-weekly-deviation-products-qty.sql`). В витрину MONEY добавлено `excess_deviation_money` (превышение нормы по модулю, миграция `add-excess-deviation-money-both-sides.sql`).

**С чего продолжить с домашнего:** открыть проект, при необходимости сказать «что делали в последнее время» — AI прочитает этот файл. Код бота и workflow закоммичены; секреты в GitHub уже настроены.
