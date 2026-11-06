# Copyright (c) 2026 Gareth Potter.
#
# SPDX-License-Identifier: Apache-2.0
"""python-thingset client against the thingset++ fixture server."""

import asyncio

import pytest
from python_thingset.response import ThingSetStatus
from python_thingset.transport.async_udp import AsyncThingSetUDPReceiver

from conftest import FIXTURE

PROPERTIES = FIXTURE["properties"]
WRITABLE = [p for p in PROPERTIES if p["writable"]]
READ_ONLY = [p for p in PROPERTIES if not p["writable"]]
FUNCTION_CASES = [
    pytest.param(f, case, id=f"{f['path']}{case['args']}")
    for f in FIXTURE["functions"]
    for case in f["cases"]
]


def _ids(items):
    return [item["path"] for item in items]


def _single_value(response):
    assert response.status_code == ThingSetStatus.CONTENT, ThingSetStatus.status_code_name(response.status_code)
    assert len(response.values) == 1
    return response.values[0].value


@pytest.mark.parametrize("prop", PROPERTIES, ids=_ids(PROPERTIES))
def test_get_by_id(client, prop):
    assert _single_value(client.get(prop["id"])) == prop["initial"]


@pytest.mark.parametrize("prop", PROPERTIES, ids=_ids(PROPERTIES))
def test_get_by_path(client, prop):
    assert _single_value(client.get(prop["path"])) == prop["initial"]


@pytest.mark.parametrize("prop", WRITABLE, ids=_ids(WRITABLE))
def test_update_by_id(client, prop):
    response = client.update(prop["id"], prop["update"], parent_id=prop["parent"])
    assert response.status_code == ThingSetStatus.CHANGED
    assert _single_value(client.get(prop["id"])) == prop["update"]


@pytest.mark.parametrize("prop", READ_ONLY, ids=_ids(READ_ONLY))
def test_update_read_only_is_rejected(client, prop):
    value = 1 if isinstance(prop["initial"], int) else "changed"
    response = client.update(prop["id"], value, parent_id=prop["parent"])
    assert response.status_code is not None and response.status_code >= ThingSetStatus.BAD_REQUEST
    assert _single_value(client.get(prop["id"])) == prop["initial"]


def test_reset_restores_initial_values(client):
    prop = WRITABLE[0]
    client.update(prop["id"], prop["update"], parent_id=prop["parent"])
    reset = next(f for f in FIXTURE["functions"] if f["path"] == "xReset")
    client.exec(reset["id"], [])
    assert _single_value(client.get(prop["id"])) == prop["initial"]


@pytest.mark.parametrize("function,case", FUNCTION_CASES)
def test_exec(client, function, case):
    response = client.exec(function["id"], case["args"])
    assert response.status_code == ThingSetStatus.CHANGED
    assert response.data == case["result"]


def test_get_unknown_id_is_not_found(client):
    assert client.get(FIXTURE["unknownId"]).status_code == ThingSetStatus.NOT_FOUND


@pytest.mark.parametrize("group", FIXTURE["groups"], ids=[g["name"] for g in FIXTURE["groups"]])
def test_fetch_group_child_ids(client, group):
    expected = [p["id"] for p in PROPERTIES if p["parent"] == group["id"]]
    response = client.fetch(group["id"], [])
    assert response.status_code == ThingSetStatus.CONTENT
    assert sorted(response.values[0].value) == sorted(expected)


def test_discovered_schema_matches_fixture(client):
    """Catches drift between fixture.json and what the server really exposes."""
    schema = client.discover_schema()
    for group in FIXTURE["groups"]:
        node = schema.by_id.get(group["id"])
        assert node is not None, f"group {group['name']} missing"
        assert (node.path, node.type) == (group["name"], "group")
    for item in PROPERTIES + FIXTURE["functions"]:
        node = schema.by_id.get(item["id"])
        assert node is not None, f"{item['path']} missing"
        assert (node.path, node.type) == (item["path"], item["type"])


def test_receives_reports():
    async def first_report():
        async with AsyncThingSetUDPReceiver(port=FIXTURE["udpPort"]) as receiver:
            _, report = await asyncio.wait_for(receiver.__anext__(), timeout=2.0)
            return report

    # Not using the client fixture: reports don't need a connection, but the server must be up.
    report = asyncio.run(first_report())
    expected = {p["id"]: p for p in PROPERTIES if p["id"] in FIXTURE["reports"]["ids"]}
    assert set(report.values) == set(expected)


@pytest.fixture(autouse=True)
def _server_running(request):
    # Every test needs the server, including the ones without a client.
    request.getfixturevalue("server")
