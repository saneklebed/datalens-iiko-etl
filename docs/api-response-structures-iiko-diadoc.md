# Уточнения по полям ответов: iiko (export incomingInvoice, pricelist) и Диадок (структура УПД)

Документ для проектирования таблиц и API в стороннем проекте. Все поля приведены по факту разбора ответов в плагине RoomBroom (парсеры в коде).

---

## 1. iiko: Export incomingInvoice (список приходных накладных за период)

**Запрос:**  
`GET {baseUrl}/resto/api/documents/export/incomingInvoice?from=YYYY-MM-dd&to=YYYY-MM-dd&key={key}`  
Параметры `from` и `to` включительно (по дате документа).

**Ответ:** XML. Корневой элемент не фиксируется в парсере; разбор идёт по **дочерним элементам с локальным именем `document`** (регистр не учитывается: `document` / `Document`).

### Один элемент списка — элемент `<document>` (или `<Document>`)

| Поле в нашей модели | XML-элемент (первый вариант) | XML-элемент (альтернатива, PascalCase) | Тип / примечание |
|---------------------|------------------------------|----------------------------------------|-------------------|
| IncomingDocumentNumber | `incomingDocumentNumber` | `IncomingDocumentNumber` | string — номер входящего документа (из ЭДО/УПД). Ключ для сопоставления с Диадоком. |
| DocumentNumber | `documentNumber` | `DocumentNumber` | string — номер накладной в iiko. |
| Status | `status` | `Status` | string — статус документа: `NEW`, `PROCESSED`, `DELETED`. |
| SupplierId | `supplier` | `Supplier` | string — GUID поставщика в iiko (если заполнен). |

**Правило парсинга в коде:** для каждого элемента проверяется `el.Name.LocalName == "document"`; внутри берутся дочерние элементы с именами в двух вариантах (camelCase и PascalCase). Строка в список попадает, если заполнены `incomingDocumentNumber` или `documentNumber`.

**Рекомендуемая таблица (пример):**

| Колонка | Тип | Описание |
|---------|-----|----------|
| incoming_document_number | text | Номер входящего документа (внешний). |
| document_number | text | Номер накладной в iiko. |
| status | text | NEW / PROCESSED / DELETED. |
| supplier_id | text | GUID поставщика. |

---

## 2. iiko: Прайс-лист поставщика (pricelist)

**Запрос:**  
`GET {baseUrl}/resto/api/suppliers/{supplierIdOrCode}/pricelist?date=dd.MM.yyyy&key={key}`  
Параметр `date` опционален; если нужен срез на дату документа — передавать в формате `dd.MM.yyyy`.

**Ответ:** XML. Разбор по **всем элементам** с локальным именем `supplierPriceListItemDto` или `item` (регистр не учитывается).

### Одна строка прайс-листа — элемент `<supplierPriceListItemDto>` или `<item>`

| Поле в нашей модели | XML-источник | Альтернативные имена элементов | Тип / примечание |
|---------------------|--------------|---------------------------------|-------------------|
| NativeProduct | дочерний элемент | `nativeProduct`, `NativeProduct` | string — GUID нашего товара в iiko. |
| NativeProductNum | дочерний элемент | `nativeProductNum`, `NativeProductNum` | string — артикул нашего товара. |
| NativeProductName | дочерний элемент | `nativeProductName`, `NativeProductName`, `productName`, `name` | string — наименование. |
| SupplierProductNum | дочерний элемент | `supplierProductNum`, `SupplierProductNum` | string — артикул у поставщика. |
| SupplierProductCode | дочерний элемент | `supplierProductCode`, `SupplierProductCode` | string — код у поставщика. |
| ContainerName | вложенный элемент `container` | `container/name`, `container/Name` | string — наименование фасовки. |
| ContainerId | элемент или вложенный | `containerId`, `ContainerId`; либо `container/id`, `container/Id` | string — GUID фасовки. |
| AmountUnitId | элемент или вложенный | `amountUnit` (если не XML-фрагмент), `AmountUnit`; либо `amountUnit/id`, `amountUnit/Id` | string — GUID единицы измерения. Если значение содержит `<`, в коде отбрасывается (чтобы не принять вложенный XML как строку). |

**Вложенная структура:**
- `container` (или `Container`) — дочерний элемент; внутри: `name`/`Name`, `id`/`Id`.
- `amountUnit` (или `AmountUnit`) — дочерний элемент; внутри: `id`/`Id`. Либо на верхнем уровне строка `amountUnit` (GUID), если не тег.

**Правило отбора строк:** строка попадает в результат, если заполнены `nativeProduct` или `nativeProductNum` (иначе `continue`).

**Рекомендуемая таблица (пример):**

| Колонка | Тип | Описание |
|---------|-----|----------|
| native_product | uuid/text | GUID нашего товара. |
| native_product_num | text | Артикул нашего товара. |
| native_product_name | text | Наименование. |
| supplier_product_num | text | Артикул у поставщика. |
| supplier_product_code | text | Код у поставщика. |
| container_name | text | Наименование фасовки. |
| container_id | uuid/text | GUID фасовки. |
| amount_unit_id | uuid/text | GUID единицы измерения. |

**Ключ для маппинга УПД → iiko:** по `supplier_product_num` или `supplier_product_code` (в плагине поиск привязки — по коду поставщика из УПД).

---

## 3. iiko: Ответ на импорт приходной накладной (import incomingInvoice)

**Запрос:**  
`POST {baseUrl}/resto/api/documents/import/incomingInvoice?key={key}`  
Тело: XML `incomingInvoiceDto` (application/xml; charset=utf-8).

**Ответ:** XML. Корневой элемент — **`documentValidationResult`** (регистр по коду не проверяется, проверяется `root.Name.LocalName`).

### Элементы ответа (корень `documentValidationResult`)

| Поле | Элемент | Альтернатива | Тип / примечание |
|------|---------|--------------|-------------------|
| DocumentNumber | `documentNumber` | `DocumentNumber` | string — номер созданной накладной в iiko. |
| Valid | `valid` | `Valid` | boolean (парсинг через bool.TryParse). |
| Warning | `warning` | `Warning` | boolean (парсинг через bool.TryParse). |
| RawXml | — | — | весь ответ (для логирования/диагностики). |

---

## 4. Диадок: структура УПД (GetEntityContent)

**Запрос:**  
`GET https://diadoc-api.kontur.ru/V4/GetEntityContent?boxId={boxId}&messageId={messageId}&entityId={entityId}`  
Заголовок: `Authorization: DiadocAuth ddauth_api_client_id=...,ddauth_token=...`

**Ответ:** XML документа (UniversalTransferDocument). Кодировка может быть UTF-8 или windows-1251 (указывается в объявлении XML); в коде при наличии `encoding="windows-1251"` или `encoding='windows-1251'` тело перекодируется из 1251.

Поддерживаются **два формата** табличной части.

---

### 4.1 Формат 1: UniversalTransferDocument (Table / InvoiceTable + Item)

**Разметка:** в дереве документа ищутся элементы с локальным именем `Table` или `InvoiceTable`. Внутри — дочерние элементы **`Item`**.

**Один элемент строки — тег `<Item>` с атрибутами:**

| Поле в нашей модели | Атрибут Item | Тип / примечание |
|---------------------|--------------|-------------------|
| LineIndex | — | порядковый номер строки (1, 2, …). |
| SupplierProductName / Product | `Product` | string — наименование товара. |
| Unit | `Unit` | string. |
| UnitName | `UnitName` | string. |
| Quantity | `Quantity` | decimal. |
| Price | `Price` | decimal. |
| Subtotal | `Subtotal` | decimal. |
| Vat | `Vat` | decimal. |
| VatPercent | `VatRate` или `TaxRate` или `VatPercent` | строка, может быть "22%", "без НДС" и т.п.; парсинг: убрать "%", заменить "," на ".", decimal.TryParse. «без» → 0. |
| ItemVendorCode | `ItemVendorCode` | string — код/артикул у поставщика (ключ для маппинга с прайс-листом iiko). |
| ItemArticle | `ItemArticle` | string. |
| Gtin | `Gtin` | string. |
| ItemAdditionalInfo | `ItemAdditionalInfo` | string. |

**Источник кода/артикула для маппинга:** в этом формате отдельно есть `ItemVendorCode` и `ItemArticle`; в плагине для поиска в прайс-листе используется `ItemVendorCode`.

---

### 4.2 Формат 2: ФНС 5.02/5.03 (ТаблСчФакт + СведТов)

**Разметка:** в дереве ищется элемент **`ТаблСчФакт`**. Внутри — дочерние элементы **`СведТов`**.

**Один элемент строки — тег `<СведТов>`:**

| Поле в нашей модели | Источник в XML | Тип / примечание |
|---------------------|----------------|-------------------|
| LineIndex | — | порядковый номер строки. |
| SupplierProductName / Product | атрибут `НаимТов` | string — наименование товара. |
| UnitName | атрибут `НаимЕдИзм` | string. |
| Unit | атрибут `ОКЕИ_Тов` | string. |
| Quantity | атрибут `КолТов` | decimal. |
| Price | атрибут `ЦенаТов` | decimal. |
| Subtotal | атрибут `СтТовУчНал` | decimal. |
| Vat | дочерний элемент `СумНал` (текст) | decimal. |
| VatPercent | атрибут `НалСт` | строка, например "22%"; парсинг как выше. |
| ItemVendorCode, ItemArticle | атрибут **`КодТов`** в дочернем элементе **`ДопСведТов`** | в формате ФНС один и тот же код используется и как код поставщика, и как артикул. |
| Gtin, ItemAdditionalInfo | — | в этой разметке не заполняются. |

**Важно:** элемент `ДопСведТов` может быть в namespace документа; в коде проверяется и `item.GetDefaultNamespace() + "ДопСведТов"`, и `"ДопСведТов"` без namespace.

**Рекомендуемая таблица строк УПД (универсальная):**

| Колонка | Тип | Описание |
|---------|-----|----------|
| line_index | int | Номер строки. |
| product_name | text | Наименование у поставщика. |
| unit | text | Единица (ОКЕИ или аналог). |
| unit_name | text | Наименование единицы измерения. |
| quantity | numeric | Количество. |
| price | numeric | Цена. |
| subtotal | numeric | Сумма. |
| vat | numeric | Сумма НДС. |
| vat_percent | numeric | Ставка НДС, %. |
| item_vendor_code | text | Код/артикул у поставщика (ключ для прайс-листа). |
| item_article | text | Артикул (в ФНС совпадает с кодом из ДопСведТов). |
| gtin | text | Штрихкод (только формат Table/Item). |
| item_additional_info | text | Доп. сведения (только формат Table/Item). |
| utd_format | text | 'TableItem' или 'FnsTable' — какой формат использовался. |

---

## 5. Краткая сводка для API/таблиц

- **export incomingInvoice:** список элементов `document` с полями `incomingDocumentNumber`, `documentNumber`, `status`, `supplier` (GUID). Даты запроса `from`/`to` — YYYY-MM-dd, включительно.
- **pricelist:** список элементов `supplierPriceListItemDto` или `item` с полями `nativeProduct`, `nativeProductNum`, `nativeProductName`, `supplierProductNum`, `supplierProductCode`, вложенные `container` (name, id), `amountUnit` (id). Опциональный query `date=dd.MM.yyyy`.
- **import incomingInvoice response:** корень `documentValidationResult`, поля `documentNumber`, `valid`, `warning`.
- **УПД (Диадок):** два формата таблицы — (1) `Table`/`InvoiceTable` → `Item` с атрибутами в camelCase/PascalCase; (2) `ТаблСчФакт` → `СведТов` с атрибутами кириллица (`НаимТов`, `КолТов`, `НалСт`, …) и дочерний `ДопСведТов` с атрибутом `КодТов`. Кодировка ответа — UTF-8 или windows-1251.

Если позже появятся уточнения по полям (новые элементы или варианты написания), их можно дописать в этот файл и переиспользовать для проектирования таблиц и DTO в вашем проекте.
