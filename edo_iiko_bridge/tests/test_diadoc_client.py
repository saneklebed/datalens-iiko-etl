"""Тесты клиента Диадока с замоканными HTTP-ответами (реальный API не вызывается)."""
import pytest
import requests_mock

from edo_iiko_bridge.clients.diadoc_client import DIADOC_API_BASE, DiadocClient
from edo_iiko_bridge.config import DiadocConfig


@pytest.fixture
def diadoc_config():
    return DiadocConfig(api_key="test-client-id", login="user", password="secret")


@pytest.fixture
def client(diadoc_config):
    return DiadocClient(diadoc_config)


def test_authenticate_and_get_organizations(client, requests_mock):
    requests_mock.post(
        f"{DIADOC_API_BASE}/V3/Authenticate",
        text="mock-auth-token-123",
    )
    requests_mock.get(
        f"{DIADOC_API_BASE}/GetMyOrganizations",
        json={
            "Organizations": [
                {
                    "OrgId": "org-1",
                    "Boxes": [{"BoxId": "box-1@diadoc.ru"}],
                }
            ]
        },
    )
    orgs = client.get_my_organizations()
    assert len(orgs) == 1
    assert orgs[0]["Boxes"][0]["BoxId"] == "box-1@diadoc.ru"
    assert client.get_default_box_id() == "box-1@diadoc.ru"


def test_get_default_box_id_raises_when_no_orgs(client, requests_mock):
    requests_mock.post(f"{DIADOC_API_BASE}/V3/Authenticate", text="token")
    requests_mock.get(f"{DIADOC_API_BASE}/GetMyOrganizations", json={"Organizations": []})
    with pytest.raises(RuntimeError, match="Нет доступных организаций"):
        client.get_default_box_id()


def test_get_incoming_documents_returns_list(client, requests_mock):
    requests_mock.post(f"{DIADOC_API_BASE}/V3/Authenticate", text="token")
    requests_mock.get(
        f"{DIADOC_API_BASE}/GetMyOrganizations",
        json={"Organizations": [{"Boxes": [{"BoxId": "box@diadoc.ru"}]}]},
    )
    requests_mock.get(
        f"{DIADOC_API_BASE}/V3/GetDocuments",
        json={
            "Documents": [
                {"DocumentType": "UniversalTransferDocument", "DocumentNumber": "1", "MessageId": "m1", "EntityId": "e1"},
                {"DocumentType": "Invoice", "DocumentNumber": "2", "MessageId": "m2", "EntityId": "e2"},
            ]
        },
    )
    docs = client.get_incoming_documents(limit=20)
    assert len(docs) == 2
    assert docs[0]["DocumentNumber"] == "1"
    assert docs[1]["DocumentType"] == "Invoice"


def test_get_incoming_documents_with_explicit_box_id(client, requests_mock):
    requests_mock.post(f"{DIADOC_API_BASE}/V3/Authenticate", text="token")
    requests_mock.get(
        f"{DIADOC_API_BASE}/V3/GetDocuments",
        json={"Documents": []},
    )
    docs = client.get_incoming_documents(box_id="custom-box@diadoc.ru", limit=5)
    assert docs == []
    # GetDefaultBoxId/GetMyOrganizations не вызывался — только Authenticate и GetDocuments
    assert len(requests_mock.request_history) == 2
    get_docs = requests_mock.request_history[1]
    assert "GetDocuments" in get_docs.url
    assert "boxId=" in get_docs.url
