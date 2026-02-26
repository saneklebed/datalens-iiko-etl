"""Тесты клиента iiko Resto с замоканными HTTP."""
import pytest
import requests_mock

from edo_iiko_bridge.clients.iiko_resto_client import IikoRestoClient
from edo_iiko_bridge.config import IikoRestoConfig


@pytest.fixture
def iiko_config():
    return IikoRestoConfig(
        base_url="https://iiko.example",
        login="user",
        password_sha1="abc123",
        verify_ssl=True,
    )


@pytest.fixture
def client(iiko_config):
    return IikoRestoClient(iiko_config)


def test_auth_and_get_products_list(client, requests_mock):
    requests_mock.get("https://iiko.example/api/auth", text="auth-key-123")
    requests_mock.get(
        "https://iiko.example/resto/api/products",
        json=[
            {"id": "p1", "name": "Milk", "num": "ART-001"},
            {"id": "p2", "number": "ART-002", "Name": "Bread"},
        ],
    )
    products = client.get_products()
    assert len(products) == 2
    assert products[0]["id"] == "p1" and products[0]["name"] == "Milk" and products[0]["articul"] == "ART-001"
    assert products[1]["articul"] == "ART-002" and products[1]["name"] == "Bread"


def test_get_products_dict_with_items(client, requests_mock):
    requests_mock.get("https://iiko.example/api/auth", text="key")
    requests_mock.get("https://iiko.example/resto/api/products", json={"items": [{"Id": "x", "Name": "Item", "code": "C1"}]})
    products = client.get_products()
    assert len(products) == 1
    assert products[0]["id"] == "x" and products[0]["name"] == "Item" and products[0]["articul"] == "C1"


def test_get_products_404_tries_next_path(client, requests_mock):
    requests_mock.get("https://iiko.example/api/auth", text="key")
    requests_mock.get("https://iiko.example/resto/api/products", status_code=404)
    requests_mock.get("https://iiko.example/resto/api/v2/entities/list", json=[])
    products = client.get_products()
    assert products == []
