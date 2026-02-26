"""Клиент API Диадока: авторизация (логин/пароль + api_client_id), организации, входящие документы."""
from __future__ import annotations

import urllib.parse
from typing import Any

import requests

from edo_iiko_bridge.config import DiadocConfig

DIADOC_API_BASE = "https://diadoc-api.kontur.ru"


class DiadocClient:
    """Работа с API Диадока: устаревшая схема DiadocAuth (api_client_id + токен по логину/паролю)."""

    def __init__(self, config: DiadocConfig) -> None:
        self._config = config
        self._session = requests.Session()
        self._token: str | None = None

    def _auth_header(self) -> dict[str, str]:
        if not self._token:
            self._token = self._authenticate()
        return {
            "Authorization": f"DiadocAuth ddauth_api_client_id={self._config.api_key},ddauth_token={self._token}"
        }

    def _authenticate(self) -> str:
        """Получить авторизационный токен (POST /V3/Authenticate?type=password)."""
        url = f"{DIADOC_API_BASE}/V3/Authenticate"
        params = {"type": "password"}
        headers = {
            "Authorization": f"DiadocAuth ddauth_api_client_id={self._config.api_key}",
            "Content-Type": "application/json",
        }
        payload = {"login": self._config.login, "password": self._config.password}
        resp = self._session.post(url, params=params, headers=headers, json=payload, timeout=30)
        resp.raise_for_status()
        # Тело ответа — авторизационный токен (строка/байты)
        token = resp.text.strip() if resp.text else resp.content.decode("utf-8", errors="replace").strip()
        if not token:
            raise RuntimeError("Диадок вернул пустой токен авторизации")
        return token

    def get_my_organizations(self) -> list[dict[str, Any]]:
        """Список организаций и ящиков (GET /GetMyOrganizations)."""
        url = f"{DIADOC_API_BASE}/GetMyOrganizations"
        headers = {
            **self._auth_header(),
            "Accept": "application/json; charset=utf-8",
        }
        resp = self._session.get(url, headers=headers, timeout=30)
        resp.raise_for_status()
        data = resp.json()
        return data.get("Organizations") or []

    def get_default_box_id(self) -> str:
        """Ящик первой организации (для простого сценария — один ящик)."""
        orgs = self.get_my_organizations()
        if not orgs:
            raise RuntimeError("Нет доступных организаций в Диадоке")
        boxes = (orgs[0].get("Boxes") or []) if orgs else []
        if not boxes:
            raise RuntimeError("У первой организации нет ящиков в Диадоке")
        return boxes[0]["BoxId"]

    def get_documents(
        self,
        box_id: str,
        filter_category: str = "Any.InboundNotRevoked",
        count: int = 100,
        sort_direction: str = "Descending",
    ) -> dict[str, Any]:
        """Список документов (GET /V3/GetDocuments). По умолчанию — входящие неаннулированные."""
        url = f"{DIADOC_API_BASE}/V3/GetDocuments"
        params: dict[str, str | int] = {
            "boxId": box_id,
            "filterCategory": filter_category,
            "count": min(max(1, count), 100),
            "sortDirection": sort_direction,
        }
        encoded = {k: (urllib.parse.quote(str(v)) if k == "afterIndexKey" else v) for k, v in params.items()}
        headers = {
            **self._auth_header(),
            "Accept": "application/json; charset=utf-8",
        }
        resp = self._session.get(url, params=encoded, headers=headers, timeout=30)
        resp.raise_for_status()
        return resp.json()

    def get_incoming_documents(self, box_id: str | None = None, limit: int = 20) -> list[dict[str, Any]]:
        """Входящие документы (первые limit записей)."""
        bid = box_id or self.get_default_box_id()
        data = self.get_documents(
            box_id=bid,
            filter_category="Any.InboundNotRevoked",
            count=limit,
            sort_direction="Descending",
        )
        return data.get("Documents") or []

    def get_entity_content(self, box_id: str, message_id: str, entity_id: str) -> bytes:
        """Содержимое сущности документа (GET /V4/GetEntityContent). Возвращает сырые байты (обычно XML)."""
        url = f"{DIADOC_API_BASE}/V4/GetEntityContent"
        params = {"boxId": box_id, "messageId": message_id, "entityId": entity_id}
        headers = self._auth_header()
        resp = self._session.get(url, params=params, headers=headers, timeout=60)
        resp.raise_for_status()
        return resp.content


__all__ = ["DiadocClient", "DIADOC_API_BASE"]
