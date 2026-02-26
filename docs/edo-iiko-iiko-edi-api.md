# EDI API iiko (REST) — системы EDI и заказы/накладные

Краткая выжимка из документации iiko API: **подключение к API EDI**, системы EDI по умолчанию и методы для заказов и накладных. Используется для интеграции моста ЭДО ↔ iiko (в т.ч. Диадок → iiko).

---

## Подключение к API EDI

Чтобы работать с API EDI в iiko, в iikoOffice должна быть создана **система EDI**. Участник EDI получает свой **GUID системы EDI (EdiSystem)** — его нужно указывать в запросах.

- **Настройки:** iikoOffice → **Обмен данными → Системы EDI**
- **Привязка к поставщику:** **Поставщики → Карточка поставщика → Дополнительные сведения → Система EDI**

При обращении к внешним заказам через API заказы фильтруются по выбранной системе EDI; в запросе обязательно указывать `ediSystem`.

---

## Системы EDI по умолчанию (чистая база)

При старте на чистой базе создаются три системы EDI:

| Название            | GUID (EdiSystem)                          |
|---------------------|-------------------------------------------|
| Внешняя система EDI | `709f5a71-47b6-f2fc-b5a8-d10176a851d7`   |
| Внутренняя система EDI | `b478869a-2c10-c398-5600-cd9202db4cd7` |
| **Контур EDI**      | `947385b3-1f5f-1074-249a-ba09b8eb1d64`   |

Для интеграции с **Контур.Диадок** логично использовать систему **Контур EDI**, если в iiko она уже привязана к нужным поставщикам/сценариям.

---

## Базовый URL

Все методы: **`https://host:port/resto/api/edi/{ediSystem}/...`**

Тот же хост/порт и авторизация, что и для остального Resto API (логин + пароль/SHA1 и т.п., по документации iiko).

---

## Методы API (версия 5.0)

### 1. Список заказов по поставщику

**GET**  
`/resto/api/edi/{ediSystem}/orders/bySeller?gln={sellerGln}&inn={sellerInn}&kpp={sellerKpp}&name={sellerName}`

| Параметр   | Описание |
|------------|----------|
| `ediSystem` | GUID системы EDI (в пути) |
| `gln`       | GLN поставщика. Если нет — обязательно указать `inn` |
| `inn`       | ИНН поставщика. Если нет — обязательно указать `gln` |
| `kpp`       | КПП (необязательно) |
| `name`      | Имя поставщика (необязательно) |

**Ответ:** массив `ediMessageDto` — список заказов EDI для участника `ediSystem` и указанного поставщика. В списке есть и отменённые на стороне iiko заказы, получение которых участник уже подтвердил. Подтверждение получения нужно отправлять методом **orders/ack**.

**Пример:**  
`GET .../resto/api/edi/709f5a71-47b6-f2fc-b5a8-d10176a851d7/orders/bySeller?gln=4545646546454`

---

### 2. Подтверждение получения заказа (квитирование)

**PUT**  
`/resto/api/edi/{ediSystem}/orders/ack?number={number}&date={date}&status={status}`

| Параметр   | Описание |
|------------|----------|
| `number`   | Номер документа |
| `date`     | Дата документа в формате **YYYY-MM-DD** |
| `status`   | `original` — подтверждение получения заказа (по умолчанию); `canceled` — подтверждение получения отмены заказа со стороны iiko |

После успешного вызова документ переходит в статус «отправленный» и перестаёт попадать в список заказов (GET orders/bySeller).

**Пример:**  
`PUT .../resto/api/edi/709f5a71-47b6-f2fc-b5a8-d10176a851d7/orders/ack?number=10001&date=2016-06-02&status=original`

---

### 3. Подтверждение внешнего заказа (ответ поставщика)

**PUT**  
`/resto/api/edi/{ediSystem}/response`

Тело запроса — структура **EdiMessageDto**. Поставщик может:
- **подтвердить** позицию — те же поля + подтверждённые значения;
- **отменить** позицию — количество 0;
- **добавить** позицию — `orderlineNumber = null`, `lineNumber` не из текущего заказа;
- **уточнить** — сначала отменить (обнулить), затем добавить новую позицию.

**Ответ:** EdiMessageDto (подтверждение внешнего заказа).

---

### 4. Выполнение заказа (отгрузка, счёт-фактура в iiko)

**PUT**  
`/resto/api/edi/{ediSystem}/invoice?senderId={senderId}`

| Параметр   | Описание |
|------------|----------|
| `senderId` | GUID пользователя (в query) |
| Тело       | **EdiMessageDto** (XML) |

**Ответ:** подтверждение отгрузки, счёт-фактура уходит в iiko.

Это ключевой метод для сценария **«накладная из ЭДО (Диадок) → приход в iiko»**: мост получает УПД из Диадока, преобразует его в формат **ediMessage** (XML) и отправляет в iiko через **PUT …/invoice**.

Пример тела запроса (фрагмент XML):

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<ediMessage id="0000301540" creationDateTime="2016-07-08T17:17:05">
    <header>
        <sender><gln>...</gln><name>...</name><internalCode/></sender>
        <recipient><gln>...</gln><name>...</name><inn/><kpp/></recipient>
        <documentType>ORDRSP</documentType>
    </header>
    <invoice number="10001" date="2016-07-08" type="Original">
        <originOrder number="10001" date="2016-06-02" type="Original"/>
        <seller>...</seller>
        <buyer>...</buyer>
        <invoicee>...</invoicee>
        <deliveryInfo>...</deliveryInfo>
        <lineItems>
            <lineItem>
                <orderLineNumber>1</orderLineNumber>
                <lineNumber>1</lineNumber>
                <internalBuyerCode>00001</internalBuyerCode>
                <name>Товар1</name>
                <requestedQuantity><measureUnit>KG</measureUnit><quantity>1.000000000</quantity></requestedQuantity>
                <confirmedQuantity>...</confirmedQuantity>
                <priceWithVat>3.000000000</priceWithVat>
                <sumWithVat>3.000000000</sumWithVat>
            </lineItem>
            <!-- ... -->
        </lineItems>
    </invoice>
</ediMessage>
```

---

## Тестовая база (из документации)

- **EdiSystem (поставщик):** Внешняя система EDI — `709F5A71-47B6-F2FC-B5A8-D10176A851D7`
- **Имя поставщика:** Вася
- **GLN поставщика:** 4545646546454
- **Номер документа:** 10001, **дата:** 2016-06-02
- **Версия iiko:** 5.0

Файл тестовой базы с настроенными Customer/Products для Chain: **ChainEdiTest.zip**.

---

## Связь с мостом ЭДО ↔ iiko

| Что делаем в мосте | Метод iiko EDI API |
|--------------------|--------------------|
| «Скачать входящие заказы» из iiko (для сверки/отчёта) | GET `orders/bySeller` |
| Сообщить iiko: «заказ получен» | PUT `orders/ack` |
| Ответ поставщика по заказу (подтверждение/отмена/добавка позиций) | PUT `response` |
| **Передать накладную/отгрузку из Диадока в iiko** | PUT `invoice` (тело — ediMessage XML) |

Для сценария **Диадок УПД → приход в iiko** основной шаг: **разобрать УПД из Диадока → сформировать ediMessage (XML) → PUT /resto/api/edi/{ediSystem}/invoice**. Систему EDI (например, Контур EDI) и привязку поставщиков нужно настроить в iikoOffice.
