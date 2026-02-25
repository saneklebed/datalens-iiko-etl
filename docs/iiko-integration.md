# Интеграция с iiko (OLAP) в проекте `datalens-iiko-etl`

Этот файл собирает в одном месте всю техническую информацию о том, **как устроена интеграция с iiko** в проекте: от переменных окружения и запроса OLAP до загрузки в Neon и особенностей периода.

## Общая схема

- **Источник:** iiko OLAP API (преднастроенный отчёт `TRANSACTIONS`).
- **ETL-скрипт:** `etl.py` (Python).
- **База назначения:** Neon (PostgreSQL), таблица `inventory_raw.olap_postings`.
- **Дальше по потоку:** `inventory_core.*` → `inventory_mart.*` → DataLens.

Код интеграции сосредоточен в:
- `etl.py`
- `.cursor/rules/datalens-iiko-knowledge.md` (раздел про iiko/OLAP)
- `README.md` (переменные окружения и общая схема)

## Переменные окружения

Используются три блока переменных:

- **Neon (PostgreSQL):**
  - `NEON_HOST`
  - `NEON_DB`
  - `NEON_USER`
  - `NEON_PASSWORD`

- **iiko API:**
  - `IIKO_BASE_URL` — базовый URL инстанса iiko (`https://example.iiko.it` или похожий), без завершающего `/`.
  - `IIKO_LOGIN` — логин.
  - `IIKO_PASS_SHA1` — **SHA1-хэш пароля**, а не сам пароль.
  - `IIKO_VERIFY_SSL` — проверка сертификата (по умолчанию включена; `0`/`false` отключают).

- **Отчёт / фильтры:**
  - `REPORT_ID` — идентификатор отчёта (идёт в колонку `report_id` в RAW).
  - `TRANSACTION_TYPES` — список типов транзакций (через запятую или `;`).
  - `PRODUCT_TYPES` — список типов продуктов (через запятую или `;`).

- **Необязательные:**
  - `RAW_DIR` — директория для сырых данных (по умолчанию `src/data/raw`).
  - `DATE_FROM`, `DATE_TO` — ручной период выгрузки в формате `YYYY-MM-DD`. При их отсутствии период считается автоматически (см. ниже).

## Логика периода (date_from / date_to)

Функция `last_closed_week_tue_to_tue` в `etl.py` вычисляет **последнюю закрытую неделю вторник → вторник**:

- если `DATE_FROM` и `DATE_TO` **не заданы**, берётся:
  - `date_from` = предыдущий вторник минус 7 дней;
  - `date_to` = предыдущий вторник.
- если заданы **оба** `DATE_FROM` и `DATE_TO`, используются они;
- если задан только один из них — выбрасывается ошибка.

Особенность iiko:

- В фильтре `DateTime.OperDayFilter` используется:
  - `includeLow = true`
  - `includeHigh = false`
- Поэтому **дата `date_to` в выгрузку не входит**. Для недели 20.01–26.01 нужно указывать `date_to = 27.01`, иначе инвентаризация не попадёт.

## Аутентификация в iiko

Функция `get_iiko_key(cfg: Config)`:

- Берёт `cfg.iiko_base_url`, `cfg.iiko_login`, `cfg.iiko_pass_sha1`.
- Пробует по очереди два эндпоинта (оба `GET`):
  - `${base}/api/auth`
  - `${base}/resto/api/auth`
- Параметры:
  - `login` — логин;
  - `pass` — SHA1-хэш пароля.
- При первом успешном ответе `200` с непустым телом возвращает токен (`resp.text.strip()`).
- Если оба запроса неудачные — кидает `RuntimeError("iiko auth failed")`.

Вызов ключа обёрнут в проверку сертификата `verify=cfg.iiko_verify_ssl` и таймаут `30` секунд.

## Запрос к OLAP API

Функция `build_olap_request(cfg: Config) -> Dict[str, Any]` формирует JSON для преднастроенного отчёта:

```json
{
  "reportType": "TRANSACTIONS",
  "buildSummary": false,
  "groupByRowFields": [
    "Department",
    "DateTime.Typed",
    "TransactionType",
    "Product.Num",
    "Product.Name",
    "Product.Category",
    "Product.MeasureUnit",
    "Contr-Account.Name"
  ],
  "groupByColFields": [],
  "aggregateFields": [
    "Amount.Out",
    "Amount.In",
    "Sum.Outgoing",
    "Sum.Incoming"
  ],
  "filters": {
    "DateTime.OperDayFilter": {
      "filterType": "DateRange",
      "periodType": "CUSTOM",
      "from": "DATE_FROMT00:00:00.000",
      "to": "DATE_TOT00:00:00.000",
      "includeLow": true,
      "includeHigh": false
    },
    "TransactionType": {
      "filterType": "IncludeValues",
      "values": ["... из TRANSACTION_TYPES ..."]
    },
    "Product.Type": {
      "filterType": "IncludeValues",
      "values": ["... из PRODUCT_TYPES ..."]
    }
  }
}
```

В реальном коде `from` и `to` подставляются как:

- `from = f"{cfg.date_from}T00:00:00.000"`
- `to = f"{cfg.date_to}T00:00:00.000"`

### Вызов OLAP

Функция `fetch_olap(cfg: Config, body: Dict[str, Any]) -> Dict[str, Any]`:

- Получает ключ через `get_iiko_key`.
- Делает `POST`:
  - URL: `${cfg.iiko_base_url}/resto/api/v2/reports/olap?key={key}`
  - Тело: `json=body`
  - Заголовки: `Content-Type: application/json`
  - `verify=cfg.iiko_verify_ssl`, `timeout=180`
- При `status_code != 200` кидает `RuntimeError(resp.text)`.
- При успехе возвращает `resp.json()`.

## Нормализация данных из OLAP

Функция `normalize(cfg: Config, data: List[Dict[str, Any]]) -> List[Dict[str, Any]]`:

- Идёт по массиву строк `data` из ответа OLAP.
- Для каждой строки:
  - Парсит дату:
    - `DateTime.Typed` → `parse_posting_dt(raw)`:
      - если строка заканчивается на `Z`, заменяет на `+00:00`;
      - парсит через `datetime.fromisoformat`;
      - если таймзоны нет — выставляет `Europe/Moscow`;
      - далее нормализует в `UTC` при формировании `posting_norm`.
  - Приводит количественные и денежные поля к `float`:
    - `Amount.Out`, `Amount.In`, `Sum.Outgoing`, `Sum.Incoming`.
  - Собирает `payload` с полями:
    - `report_id`, `date_from`, `date_to` (из конфига);
    - `department` (`Department`);
    - `posting_dt` (датавремя в локальной TZ, далее в БД также сохраняется как есть);
    - `product_num` (`Product.Num`), `product_name`, `product_category`, `product_measure_unit`;
    - `contr_account_name` (`Contr-Account.Name`);
    - `transaction_type` (`TransactionType`);
    - `amount_out`, `amount_in`, `sum_outgoing`, `sum_incoming`.
  - Считает `source_hash` как SHA256 от JSON-представления `payload` + нормализованного времени:
    - `json.dumps({**payload, "posting_dt": posting_norm}, sort_keys=True)`.
- Возвращает список нормализованных строк с полем `source_hash`.

Особенности:

- При любой ошибке парсинга строки (дата, числа и т.п.) строка **пропускается** (`continue`).
- Таймзоны приводятся к UTC для `posting_norm`, но в сам payload идёт локальное `posting_dt`.

## Загрузка в Neon (PostgreSQL)

### Подключение

Функция `db_connect(cfg: Config)` создаёт соединение:

- `host=NEON_HOST`
- `dbname=NEON_DB`
- `user=NEON_USER`
- `password=NEON_PASSWORD`
- `sslmode="require"`

### Очистка периода

`delete_period(cfg: Config) -> int`:

- Выполняет:

```sql
delete from inventory_raw.olap_postings
where report_id = %s and date_from = %s and date_to = %s;
```

- Удаляет все строки за заданный период/отчёт перед перезаписью;
- Возвращает количество удалённых строк (логируется в `main()`).

### Вставка строк

`insert_rows(cfg: Config, rows: List[Dict[str, Any]])`:

- Если `rows` пустой — ничего не делает.
- Формирует батч вставки в `inventory_raw.olap_postings` по схеме:

```sql
insert into inventory_raw.olap_postings
(report_id, date_from, date_to, department, posting_dt,
 product_num, product_name, product_category, product_measure_unit,
 contr_account_name, transaction_type,
 amount_out, amount_in, sum_outgoing, sum_incoming,
 source_hash, loaded_at)
values %s
on conflict (source_hash) do nothing;
```

- Использует `psycopg2.extras.execute_values` с `page_size=1000`.
- `loaded_at` проставляется как `datetime.now(timezone.utc)`.

**Идемпотентность:**  
За счёт `source_hash` и `ON CONFLICT DO NOTHING` повторный запуск ETL не создаёт дубли по тем же строкам.

## Поток выполнения `etl.py`

Функция `main()`:

1. Загружает конфиг `cfg = load_config()`.
2. Печатает в консоль период: `[period] YYYY-MM-DD → YYYY-MM-DD`.
3. Строит тело отчёта: `body = build_olap_request(cfg)`.
4. Тянет данные из iiko: `resp = fetch_olap(cfg, body)`.
5. Нормализует: `rows = normalize(cfg, resp.get("data") or [])`.
6. Удаляет старые строки за период: `deleted = delete_period(cfg)`.
7. Вставляет новые строки: `insert_rows(cfg, rows)`.
8. Логирует количество вставленных строк: `[done] rows inserted: N`.

Запуск локально:

- Установить зависимости: `pip install -r requirements.txt`.
- Создать `.env` с нужными переменными (см. раздел «Переменные окружения»).
- Запустить: `python etl.py`.

Запуск в GitHub Actions:

- workflow `.github/workflows/etl.yml`:
  - подставляет `REPORT_ID`, `TRANSACTION_TYPES`, `PRODUCT_TYPES` и переменные iiko / Neon из секретов;
  - позволяет задать `DATE_FROM` / `DATE_TO` через `Run workflow` для ручной выгрузки периодов.

## Особенности и грабли интеграции

Критичные моменты по интеграции с iiko:

- **Период:** `date_to` в запросе **не включается** (пара `includeLow=true`, `includeHigh=false`). Для полной недели всегда указывать `date_to = последний_день + 1`.
- **SHA1-пароль:** в аутентификацию передаётся SHA1-хэш пароля (`IIKO_PASS_SHA1`), а не сам пароль.
- **Таймзоны:** в ответе iiko могут быть даты с суффиксом `Z` (UTC) или без таймзоны. В коде всё нормализуется к UTC для хэша/загрузки.
- **Итоговые строки:** строки с «Итого»/«Всего» нужно фильтровать при нормализации (в текущем коде пропускаются строки, которые не парсятся корректно; при расширении логики важно не пропустить такие маркеры).
- **Дубликаты:** защита от дублей реализована на уровне БД через `source_hash` и `ON CONFLICT DO NOTHING`.
- **Связка с остальной моделью:** всё, что касается пересорта, Порчи, инвентаризаций и т.п., рассчитывается уже в слоях `inventory_core` и `inventory_mart`. RAW (`inventory_raw.olap_postings`) — это «чистый» OLAP-поток из iiko без бизнес-логики.

## Полезные ссылки по iiko

- Официальная документация iiko API: `https://api-ru.iiko.services/docs`
- OLAP-отчёты v2: `http://ru.iiko.help/articles/#!api-documentations/prednastroennye-olap-otchety-vv2`
- Практическое руководство по OLAP: `https://open-s.info/blog/olap_instruktsiya/`

