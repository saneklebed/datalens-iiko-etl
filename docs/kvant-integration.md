# Интеграция с Kvant API (создание коммуникации из alerts‑бота)

Этот файл описывает, как настроена интеграция Telegram‑бота с задачником **Kvant**. Бот после рассылки еженедельного отчёта создаёт в Кванте тестовую коммуникацию «Тестовая коммуникация».

## 1. Получение API‑ключа и user id в Кванте

1. **API‑ключ:**
   - В веб‑интерфейсе Кванта зайти в свой профиль.
   - Перейти в **настройки** и создать новый API‑ключ.
   - Этот ключ используется только на чтение/запись задач через REST API.

2. **ID пользователя (assignee):**
   - Открыть страницу пользователя в Кванте.
   - В адресной строке будет вид:  
     `https://orexpress.kvant.app/user/show/34976`  
     Здесь `34976` — это `to_user_id` (id пользователя, на которого будет вешаться коммуникация).

## 2. Настройка секретов в GitHub

В репозитории `datalens-iiko-etl` в GitHub:

- Перейти в **Settings → Secrets and variables → Actions → New repository secret**.
- Создать два секрета:

1. `KVANT_API_KEY` — строка с API‑ключом из профиля Кванта.
2. `KVANT_ASSIGNEE_ID` — id пользователя, например `34976` (как в URL `/user/show/34976`).

Эти секреты используются в workflow `alerts.yml` и прокидываются в окружение раннера.

## 3. Конфиг и код в `alerts_bot.py`

### 3.1. Конфигурация бота

В `alerts_bot.py` структура `BotConfig` расширена полями:

- `kvant_api_key: Optional[str]`
- `kvant_assignee_id: Optional[int]`

Функция `load_config()` читает их из переменных окружения:

```python
kvant_api_key=os.getenv("KVANT_API_KEY"),
kvant_assignee_id=int(os.getenv("KVANT_ASSIGNEE_ID")) if os.getenv("KVANT_ASSIGNEE_ID") else None,
```

Также объявлена константа с URL метода создания коммуникации:

```python
KVANT_TASKS_STORE_URL = "https://platform.kvant.app/openapi/tasks/store"
```

Это соответствует эндпоинту из OpenAPI Кванта: `POST /tasks/store`.

### 3.2. Вызов Kvant API

Функция `send_kvant_test_message(cfg: BotConfig, text: str)`:

- Ничего не делает, если нет `kvant_api_key` или `kvant_assignee_id`.
- Собирает заголовки авторизации по схеме `api-key` из Swagger:

```python
headers = {
    "api-key": cfg.kvant_api_key,      # Name: api-key, In: header
    "Content-Type": "application/json",
}
```

- Тело запроса строится по примеру из документации; в `value` передаётся текст, который формирует бот
  (сводка + отчёты по филиалам):

```python
payload = {
    "to_user_id": cfg.kvant_assignee_id,
    "due_at": None,
    "required_deadline": 0,
    "type_id": 1,
        "inputs_values": [
            {"value": text, "task_input_id": 1},
        ],
    "function_user_id": None,
    "task_labels": None,
    "relation_track_users": [
        {"id": cfg.kvant_assignee_id, "user_type": 1},
    ],
    "program_id": None,
}
```

- Отправка запроса:

```python
resp = requests.post(
    KVANT_TASKS_STORE_URL,
    json=payload,
    headers=headers,
    timeout=10,
)
resp.raise_for_status()
```

- Ошибки логируются, но не ломают workflow:

```python
except Exception as e:
    status = getattr(getattr(e, "response", None), "status_code", None)
    print(f"[kvant] error sending communication: {e!r}, status={status}")
```

### 3.3. Когда вызывается Kvant

В `main()` в ветке `ALERTS_MODE != "bot"` (режим GitHub Actions / one-shot):

1. Бот собирает отчёты по филиалам и сводку.
2. Отправляет их в Telegram в несколько сообщений.
3. После завершения `asyncio.run(send())` вызывается:

```python
send_kvant_test_message(cfg)
```

То есть интеграция с Квантом идёт **после** успешной рассылки отчёта в Telegram.

## 4. Workflow GitHub Actions

В файле `.github/workflows/alerts.yml`:

- В блоке `env` для job `send-alerts` добавлены:

```yaml
      # Kvant API
      KVANT_API_KEY: ${{ secrets.KVANT_API_KEY }}
      KVANT_ASSIGNEE_ID: ${{ secrets.KVANT_ASSIGNEE_ID }}
```

- Остальная логика workflow:
  - триггеры:
    - `workflow_dispatch` — ручной запуск;
    - `schedule: 0 9 * * TUE` — каждый вторник в 12:00 по Москве (09:00 UTC);
  - шаги: checkout, установка Python, `pip install -r requirements.txt`, запуск `python alerts_bot.py`.

## 5. Как проверить, что всё работает

1. Убедиться, что в GitHub заданы секреты `KVANT_API_KEY` и `KVANT_ASSIGNEE_ID`.
2. В разделе **Actions** запустить workflow **Inventory alerts to Telegram** вручную.
3. Проверить:
   - В Telegram‑чате — пришли сообщения по филиалам и сводка.
   - В Кванте — появилась новая коммуникация с текстом **«Тестовая коммуникация»** на пользователя с id `KVANT_ASSIGNEE_ID`.
4. При проблемах с авторизацией или форматом запроса смотреть логи шага `Send alerts to Telegram` в Actions — там будут строки вида:
   - `[kvant] test communication created successfully`
   - или `[kvant] error sending communication: ...`.

