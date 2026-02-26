"""Клиент iiko Server REST: авторизация (логин + SHA1), номенклатура для маппинга."""
from __future__ import annotations

from typing import Any

import requests

from edo_iiko_bridge.config import IikoRestoConfig


class IikoRestoClient:
    """Клиент iiko Server REST. Авторизация как в ETL: GET .../resto/api/auth?login=&pass= (SHA1)."""

    def __init__(self, config: IikoRestoConfig) -> None:
        self._config = config
        self._session = requests.Session()
        self._key: str | None = None

    def _get_key(self) -> str:
        if self._key is not None:
            return self._key
        base = self._config.base_url.rstrip("/")
        login = self._config.login.strip()
        sha1 = self._config.password_sha1.strip().lower()
        for path in ("/api/auth", "/resto/api/auth"):
            url = base + path
            resp = self._session.get(
                url,
                params={"login": login, "pass": sha1},
                verify=self._config.verify_ssl,
                timeout=30,
            )
            if resp.status_code == 200 and resp.text.strip():
                self._key = resp.text.strip()
                return self._key
        raise RuntimeError("iiko auth failed")

    def _get(self, path: str, params: dict[str, str] | None = None) -> Any:
        """GET запрос к Resto API с подстановкой ключа."""
        base = self._config.base_url.rstrip("/")
        url = f"{base}/resto/{path.lstrip('/')}"
        q = dict(params or {})
        q["key"] = self._get_key()
        resp = self._session.get(url, params=q, verify=self._config.verify_ssl, timeout=60)
        resp.raise_for_status()
        if not resp.text.strip():
            return None
        return resp.json()

    def get_products(self) -> list[dict[str, Any]]:
        """Список товаров/номенклатуры для маппинга (артикул, название, id).

        Пробует типичные пути Resto API; при отсутствии эндпоинта возвращает [].
        Структура ответа зависит от версии iiko (ожидаются поля: id, name, num/number/code).
        """
        for api_path in ("api/products", "api/v2/entities/list"):
            try:
                data = self._get(api_path)
            except requests.HTTPError as e:
                if e.response is not None and e.response.status_code == 404:
                    continue
                raise
            if data is None:
                continue
            if isinstance(data, list):
                return _normalize_products(data)
            if isinstance(data, dict):
                # Некоторые API возвращают { "items": [...] } или { "products": [...] }
                for key in ("products", "items", "data", "result"):
                    if key in data and isinstance(data[key], list):
                        return _normalize_products(data[key])
            break
        return []


def _normalize_products(raw: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Приводит элементы к полям id, name, articul (артикул/номер)."""
    out = []
    for p in raw:
        if not isinstance(p, dict):
            continue
        articul = (
            p.get("num") or p.get("number") or p.get("code") or p.get("articul")
            or p.get("Product.Num") or p.get("Num") or ""
        )
        name = p.get("name") or p.get("Name") or p.get("Product.Name") or ""
        pid = p.get("id") or p.get("Id") or p.get("productId") or ""
        out.append({"id": str(pid), "name": str(name), "articul": str(articul)})
    return out


__all__ = ["IikoRestoClient"]
