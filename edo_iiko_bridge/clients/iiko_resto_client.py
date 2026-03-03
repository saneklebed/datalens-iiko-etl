"""Клиент iiko Server REST: авторизация (логин + SHA1), номенклатура для маппинга."""
from __future__ import annotations

from typing import Any

import xml.etree.ElementTree as ET

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

    def import_incoming_invoice(self, xml_body: str) -> dict[str, Any]:
        """Импорт приходной накладной (incomingInvoice) в iiko.

        Отправляет XML, совместимый с XSD incomingInvoiceDto, в эндпоинт
        POST /resto/api/documents/import/incomingInvoice?key=...
        и возвращает результат валидации как словарь
        {"documentNumber": str | None, "valid": bool | None, "warning": bool | None, "raw": str}.
        """
        base = self._config.base_url.rstrip("/")
        url = f"{base}/resto/api/documents/import/incomingInvoice"
        params = {"key": self._get_key()}
        resp = self._session.post(
            url,
            params=params,
            data=xml_body.encode("utf-8"),
            headers={"Content-Type": "application/xml; charset=utf-8"},
            verify=self._config.verify_ssl,
            timeout=60,
        )
        resp.raise_for_status()
        text = resp.text.strip()
        result: dict[str, Any] = {"raw": text or ""}
        if not text or not text.lstrip().startswith("<"):
            return result

        try:
            root = ET.fromstring(text)
        except ET.ParseError:
            return result

        if root.tag.endswith("documentValidationResult"):
            doc_number = root.findtext("documentNumber") or root.findtext("DocumentNumber")
            valid_text = root.findtext("valid") or root.findtext("Valid")
            warning_text = root.findtext("warning") or root.findtext("Warning")
            result.update(
                {
                    "documentNumber": doc_number,
                    "valid": (valid_text.lower() == "true") if isinstance(valid_text, str) else None,
                    "warning": (warning_text.lower() == "true") if isinstance(warning_text, str) else None,
                }
            )
        return result


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
