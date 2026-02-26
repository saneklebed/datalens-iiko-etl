# Мост ЭДО ↔ iiko (своя интеграция вместо платного EDI-Doc)

Сервис-мост между оператором ЭДО (Контур.Диадок) и iiko Server: получение входящих накладных/УПД из ЭДО, сопоставление с номенклатурой iiko, создание приходов в iiko.

## MVP (первая очередь)

1. **Подключение к Диадоку** — авторизация, список входящих документов, скачивание XML УПД.
2. **Подключение к iiko Server** — REST `.../resto`, получение номенклатуры/прайса поставщиков (аналог `Suppliers_price`).
3. **Хранение сопоставлений** — таблица «строка документа ЭДО ↔ товар iiko» (JSON или SQLite).
4. **Один сценарий:** «получить список входящих из Диадока → открыть один документ → показать строки для сопоставления» (без создания прихода в iiko пока).

Дальше: создание/проведение прихода в iiko по данным УПД и сохранённому маппингу.

## Окружение и секреты

Секреты храним в **GitHub Secrets**. Для iiko **новые не нужны** — используем те же, что и для ETL: `IIKO_BASE_URL`, `IIKO_LOGIN`, `IIKO_PASS_SHA1`, `IIKO_VERIFY_SSL`. Добавить в Secrets нужно только для Диадока.

| Переменная | Описание | Секрет в GitHub |
|------------|----------|-----------------|
| `DIADOC_API_KEY` | Ключ API Контур.Диадок | `DIADOC_API_KEY` — **добавить** |
| `DIADOC_LOGIN` | Логин пользователя Диадока | `DIADOC_LOGIN` — **добавить** |
| `DIADOC_PASSWORD` | Пароль для входа в Диадок | `DIADOC_PASSWORD` — **добавить** |
| `IIKO_BASE_URL` | Базовый URL iiko (`https://...iiko.it`) | уже есть |
| `IIKO_LOGIN` | Логин iiko | уже есть |
| `IIKO_PASS_SHA1` | SHA1-хэш пароля iiko | уже есть |
| `IIKO_VERIFY_SSL` | Проверка SSL (0/1) | уже есть |

URL для REST iiko Server в коде собирается как `IIKO_BASE_URL + "/resto"`. В workflow переменные подставляются из секретов (см. `.github/workflows/edo-iiko-bridge.yml`).

## Запуск

**Локально** (из репо, после `git clone`):

```bash
pip install -r requirements.txt
# задай переменные окружения или положи .env (не в репо)
python -m edo_iiko_bridge.cli fetch-incoming
```

**Из GitHub Actions** (ручной или по расписанию): вкладка Actions → workflow «EDI-Doc bridge» → Run workflow. Секреты берутся из настроек репозитория.

## Структура

- `config.py` — загрузка настроек из env.
- `clients/diadoc_client.py` — клиент API Диадока.
- `clients/iiko_resto_client.py` — клиент REST iiko Server (resto).
- `mapping_store.py` — сохранение/загрузка сопоставлений (пока JSON-файл).
- `cli.py` — точки входа для команд.
- `parsers/` — разбор XML УПД (формат ФНС/Диадок).

Документация по API: [Диадок](https://api-docs.diadoc.ru/), [iiko](https://api.iiko.ru/) (Server). По разбору партнёрского решения — `docs/edo-iiko-edidoc-reverse-summary.md`.
