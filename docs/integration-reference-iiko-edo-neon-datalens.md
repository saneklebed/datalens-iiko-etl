# Сводная документация: iiko + ЭДО + Neon + DataLens

Документ для переноса контекста в другой проект: как и куда подключались, какие запросы, важные тонкости.

---

## 1. Общая схема проектов

### datalens-iiko-etl (репозиторий)
- **Цель:** управленческий дашборд по инвентаризациям (недостачи/излишки, нормы, потери).
- **Поток:** iiko OLAP → ETL (`etl.py`) → Neon (`inventory_raw.olap_postings`) → `inventory_core` → `inventory_mart` → DataLens.

### Плагин RoomBroom (ЭДО ↔ iiko)
- **Цель:** мост между Контур.Диадок (ЭДО) и iiko Server: входящие УПД из Диадока → сопоставление с номенклатурой iiko по прайс-листу поставщика → создание приходных накладных в iiko.
- **Окружение:** .NET 4.8, WinForms, DevExpress v16.2 (из iikoChain Office). Устанавливается в `C:\Program Files\iiko\iikoChain\Office\Plugins`.

---

## 2. iiko API

### 2.1 Базовый URL и авторизация
- **Базовый URL:** из переменной `IIKO_BASE_URL` или из конфига плагина `RoomBroom.iiko.config.json` (`baseUrl`). Без завершающего `/`.
- **Ключ (key):** получается один раз, далее передаётся как query-параметр `key=...` во все запросы к REST.

**Получение ключа (пробуем по очереди):**
1. `GET {baseUrl}/api/auth?login={login}&pass={sha1}`
2. `GET {baseUrl}/resto/api/auth?login={login}&pass={sha1}`

**Важно:** в параметр `pass` передаётся **SHA1-хэш пароля** (строка в нижнем регистре), а не сам пароль.

**Источники учётных данных:**
- ETL: переменные окружения `IIKO_LOGIN`, `IIKO_PASS_SHA1`, `IIKO_BASE_URL`, `IIKO_VERIFY_SSL`.
- Плагин: JSON-файл `RoomBroom.iiko.config.json` рядом с DLL (`baseUrl`, `login`, `passwordSha1`) или те же env-переменные.

### 2.2 OLAP API (ETL — проводки для дашборда)
- **URL:** `POST {baseUrl}/resto/api/v2/reports/olap?key={key}`
- **Тело:** JSON с полями `reportType: "TRANSACTIONS"`, `groupByRowFields`, `aggregateFields`, `filters`.
- **Период (критично):** в фильтре `DateTime.OperDayFilter` задаётся `from` и `to`; используется **includeLow = true, includeHigh = false** — то есть **date_to в выборку не входит**. Для недели 20.01–26.01 нужно указывать `to = 27.01T00:00:00.000`.
- **Формат дат в теле:** `from = "YYYY-MM-DDTHH:mm:ss.000"`, `to = "YYYY-MM-DDTHH:mm:ss.000"`.

Подробно: `docs/iiko-integration.md`, `.cursor/rules/datalens-iiko-knowledge.md`.

### 2.3 REST API плагина (iiko Server, префикс /resto)

Все запросы: `{baseUrl}/resto/{path}?key={key}` (GET или POST с XML/JSON).

| Назначение | Метод | Путь | Параметры / тело |
|------------|--------|------|-------------------|
| Список поставщиков | GET | `api/suppliers` | — |
| Склады | GET | `api/corporation/stores` или `api/corporation/stores?revisionFrom=-1` (пробуются также `api/corporation/departments`, `api/stores`, `api/departments`) | — |
| Прайс-лист поставщика | GET | `api/suppliers/{supplierIdOrCode}/pricelist` | Опционально: `?date=DD.MM.YYYY` — дата документа для стабильного среза. |
| Импорт приходной накладной | POST | `api/documents/import/incomingInvoice` | Тело: XML `incomingInvoiceDto` (application/xml; charset=utf-8). |
| Экспорт приходных накладных за период | GET | `api/documents/export/incomingInvoice?from=YYYY-MM-dd&to=YYYY-MM-dd` | from/to включительно. |

**Прайс-лист поставщика (важно для маппинга):**
- В ответе XML элементы `supplierPriceListItemDto` (или `item`): `nativeProduct` (GUID нашего товара), `nativeProductNum`, `nativeProductName`, `supplierProductNum`, `supplierProductCode`, `container`/`containerId`, `amountUnit`/`amountUnitId`.
- Сопоставление строк УПД с номенклатурой iiko делается **по коду/артикулу у поставщика** (`SupplierProductNum`/`SupplierProductCode`) → подставляется `nativeProduct` в XML прихода. Прайс-лист желательно запрашивать на **дату документа** (`?date=DD.MM.YYYY`).

**Импорт приходной накладной (incomingInvoice):**
- В каждой строке прихода в iiko обязательно передавать: `product` (GUID нашего товара), при необходимости `containerId`, `amountUnit`, `vatPercent`, `vatSum`; `productArticle` — артикул **нашего** товара (`nativeProductNum`), а не поставщика.
- Документация iiko по XSD `incomingInvoiceDto` — соблюдать типы полей (например, `supplierProduct` по схеме — GUID, не текст).

### 2.4 EDI API iiko (альтернативный путь накладных)
- Базовый путь: `{baseUrl}/resto/api/edi/{ediSystem}/...`
- Система «Контур EDI» (пример): `947385b3-1f5f-1074-249a-ba09b8eb1d64`.
- Создание накладной/отгрузки: `PUT .../invoice?senderId={senderId}`, тело — ediMessage (XML). Подробно: `docs/edo-iiko-iiko-edi-api.md`.

---

## 3. Диадок (Контур.ЭДО) API

### 3.1 Базовый URL и заголовки
- **Base URL:** `https://diadoc-api.kontur.ru`
- **Авторизация:** заголовок `Authorization: DiadocAuth ddauth_api_client_id={apiToken},ddauth_token={token}`.

### 3.2 Получение токена
- **Метод:** `POST /V3/Authenticate?type=password`
- **Заголовок:** `Authorization: DiadocAuth ddauth_api_client_id={DiadocApiToken}` (без token до первого логина).
- **Тело (JSON):** `{"login": "...", "password": "..."}` — логин и пароль в открытом виде.
- В ответе — токен (строка или JSON с полем `token`). Токен далее подставляется в `ddauth_token=...`.

**Настройки плагина (RoomBroomConfig):** `DiadocLogin`, `DiadocPassword`, `DiadocApiToken` (API key из личного кабинета Диадока).

### 3.3 Основные методы (плагин)

| Назначение | Метод | Путь | Параметры |
|------------|--------|------|------------|
| Контрагенты (с пагинацией) | GET | `V3/GetCounteragents` | `myBoxId`, `counteragentStatus`, `afterIndexKey` (по 100 записей). |
| Список документов (входящие) | GET | `V3/GetDocuments` | `boxId`, `filterCategory=Any.InboundNotRevoked`, `count=100`, `sortDirection=Descending`, `fromDocumentDate`, `toDocumentDate` (формат ДД.ММ.ГГГГ), `afterIndexKey` для пагинации. |
| Содержимое документа (XML УПД) | GET | `V4/GetEntityContent` | `boxId`, `messageId`, `entityId`. |
| Подписание (подготовка) | POST | `PrepareDocumentsToSign` | тело по API. |
| Отправка подписи | POST | `V4/PostMessagePatch` | тело (patch). |
| Отказ от подписания | POST | `V2/GenerateSignatureRejectionXml` | `boxId` + тело. |

**Важно по BoxId:** из API может приходить `boxId` в виде `guid@diadoc.ru`. В запросы нужно передавать **чистый GUID** (без `@diadoc.ru`).

### 3.4 Разбор УПД (GetEntityContent)
- Ответ — XML UniversalTransferDocument (ФНС 5.02/5.03 или формат с InvoiceTable/Item).
- Плагин парсит табличную часть: наименование, артикул/код поставщика (`ItemVendorCode` из элемента `code` или аналогов), количество, цена, сумма, НДС; для ставки НДС — атрибуты `НалСт`, `VatRate` и т.п.
- Код поставщика используется как уникальный ключ для поиска в прайс-листе iiko.

**Документация Диадок:** https://api-docs.diadoc.ru/

---

## 4. Neon (PostgreSQL)

### 4.1 Подключение
- **Переменные:** `NEON_HOST`, `NEON_DB`, `NEON_USER`, `NEON_PASSWORD`
- **Параметры:** `sslmode=require`
- Используется в ETL (`etl.py`) для записи в `inventory_raw.olap_postings`.

### 4.2 Схемы и ключевые таблицы
- **inventory_raw.olap_postings** — сырые проводки из iiko OLAP. Поля: `report_id`, `date_from`, `date_to`, `department`, `posting_dt`, `product_num`, `product_name`, `product_category`, `product_measure_unit`, `transaction_type`, `amount_out`, `amount_in`, `sum_outgoing`, `sum_incoming`, `contr_account_name`, `source_hash`, `loaded_at`. Уникальность/идемпотентность: `ON CONFLICT (source_hash) DO NOTHING`.
- **inventory_core.*** — нормализованная лента, движения, инвентаризации, правила норм и т.д. (см. `docs/neon-tables.md`).
- **inventory_mart.*** — витрины под DataLens (например `weekly_deviation_products_qty`, `weekly_deviation_products_money_v2`, `weekly_product_documents_products`).

Полный перечень таблиц и DDL: `docs/neon-tables.md`, `docs/neon-schema.sql`. Обновление схемы: скрипт `scripts/dump_neon_schema.py` / `scripts/dump_neon_ddl.py` (нужен .env с NEON_*).

---

## 5. DataLens
- Подключение к Neon как к источнику данных; датасеты и чарты строятся по витринам `inventory_mart`.
- **Практики проекта:** сортировка по меркам — через обычную таблицу и поле в строках (не сводная + не RANK в нашем кейсе); агрегацию фиксировать на уровне датасета (SUM по полям), чтобы не было «дробления» строк по филиалам; фильтр по периоду — по полю `week_start` (дата), а не по `week_label`.
- Подробнее: `.cursor/rules/datalens-iiko-knowledge.md`.

---

## 6. Важные тонкости (грабли)

### iiko
- **Период OLAP:** `date_to` исключающий — для полной недели 20.01–26.01 задавать `date_to = 27.01`.
- **Пароль:** везде передавать SHA1-хэш пароля, не пароль.
- **Прайс-лист:** запрашивать на дату документа (`?date=DD.MM.YYYY`), иначе при изменении прайс-листа маппинг может разъехаться.
- **Приходная накладная (XML):** в строках передавать наш GUID товара (`product`), наш артикул в `productArticle`; при необходимости `containerId`, `amountUnit`, `vatPercent`, `vatSum` по схеме iiko.

### Диадок
- **BoxId:** убирать суффикс `@diadoc.ru` перед запросами.
- **Контрагенты:** пагинация обязательна (`afterIndexKey`), иначе только первые 100.
- **Формат дат в GetDocuments:** `ДД.ММ.ГГГГ`.

### Neon / ETL
- **source_hash:** считаем от нормализованного payload (в т.ч. датавремя в UTC) для защиты от дублей; вставка с `ON CONFLICT (source_hash) DO NOTHING`.
- **Таймзоны:** в ответе iiko даты могут быть с `Z` или без таймзоны — нормализовать к UTC для хэша и хранения.

### Плагин
- Настройки iiko для плагина: `RoomBroom.iiko.config.json` в папке плагина (baseUrl, login, passwordSha1). Логи/диагностика: `dist/iiko_import_debug.log`, исходящий XML прихода — в `dist/xml/` (если включено сохранение).

---

## 7. Полезные ссылки
- iiko API: https://api-ru.iiko.services/docs  
- iiko OLAP v2: http://ru.iiko.help/articles/#!api-documentations/prednastroennye-olap-otchety-vv2  
- Диадок API: https://api-docs.diadoc.ru/  
- DataLens: https://datalens.tech/docs/ru/

---

## 8. Файлы в репозитории (где что искать)
| Что | Где |
|-----|-----|
| ETL (iiko OLAP → Neon) | `etl.py`, `docs/iiko-integration.md` |
| Плагин ЭДО ↔ iiko | `edo_iiko_bridge/RoomBroomChainPlugin/` |
| Клиент iiko REST | `edo_iiko_bridge/RoomBroomChainPlugin/Iiko/IikoRestoClient.cs` |
| Клиент Диадок | `edo_iiko_bridge/RoomBroomChainPlugin/Diadoc/DiadocApiClient.cs` |
| Схема/таблицы Neon | `docs/neon-tables.md`, `docs/neon-schema.sql` |
| EDI API iiko (альтернатива) | `docs/edo-iiko-iiko-edi-api.md` |
| База знаний DataLens + iiko | `.cursor/rules/datalens-iiko-knowledge.md` |
| План интеграции ЭДО–iiko | `docs/edo-iiko-integration-plan.md` |
