"""Execute the actual generated package against Pydantic and HTTPX."""

import asyncio
import compileall
import importlib
import inspect
import json
import pathlib
import pkgutil
import sys

import httpx
from pydantic import ValidationError


root = pathlib.Path(__file__).parent
assert compileall.compile_dir(root / "generated", quiet=1)
models = importlib.import_module("generated.models")
for module in pkgutil.iter_modules(models.__path__, models.__name__ + "."):
    importlib.import_module(module.name)

if sys.argv[1] == "imports":
    assert models.Child.model_validate({"id": "base", "name": "child"}).id == "base"
    assert models.GrandChild.model_validate({"id": "base", "name": "child", "label": "grand"}).label == "grand"
    print("generated model imports and inherited validation passed")
    raise SystemExit(0)

Envelope = models.Envelope
Child = models.Child
Status = models.Status
NodeA = models.NodeA
payload = {
    "status": "ready",
    "children": [{"display-name": "Alice", "score": 1.5}],
    "byName": {"first": {"display-name": "Bob", "score": 2.5}},
    "requiredNullable": None,
    "optionalNullable": None,
    "cycle": {"name": "a", "next": {"value": 2, "next": {"name": "tail"}}},
}
envelope = Envelope.model_validate(payload)
assert isinstance(envelope.status, Status)
assert isinstance(envelope.children[0], Child)
assert isinstance(envelope.by_name["first"], Child)
assert isinstance(envelope.cycle, NodeA)
assert envelope.cycle.next.next.name == "tail"
assert "optional_non_null" not in envelope.model_fields_set
assert "optional_nullable" in envelope.model_fields_set
assert json.loads(envelope.model_dump_json(by_alias=True, exclude_unset=True)) == payload


def rejected(value, model=Envelope):
    try:
        model.model_validate(value)
    except ValidationError:
        return
    raise AssertionError(f"Invalid payload was accepted: {value!r}")


rejected({key: value for key, value in payload.items() if key != "requiredNullable"})
rejected({**payload, "optionalNonNull": None})
rejected({**payload, "status": "unknown"})
for name in ("A", "A1", "TooLongAlphabetic"):
    rejected({"display-name": name, "score": 1.5}, Child)
for score in (0.49, 10.5):
    rejected({"display-name": "Alice", "score": score}, Child)
cyclic = {"name": "loop"}
cyclic["next"] = {"value": 1, "next": cyclic}
rejected(cyclic, NodeA)

api_package = importlib.import_module("generated.api")
client_types = []
for module in pkgutil.iter_modules(api_package.__path__, api_package.__name__ + "."):
    imported = importlib.import_module(module.name)
    client_types.extend(
        candidate for _, candidate in inspect.getmembers(imported, inspect.isclass)
        if candidate.__module__ == imported.__name__ and hasattr(candidate, "save_record")
    )
assert len(client_types) == 1
Client = client_types[0]


class RefreshOnce(httpx.Auth):
    requires_request_body = True

    def __init__(self):
        self.refreshes = 0

    def auth_flow(self, request):
        request.headers["Authorization"] = "Bearer old"
        response = yield request
        if response.status_code == 401:
            self.refreshes += 1
            request.headers["Authorization"] = "Bearer refreshed"
            yield request


requests = []
hooks = []


def handle(request):
    requests.append((request.method, request.url.raw_path, dict(request.headers), request.content))
    assert request.headers["X-Application"] == "shared"
    if request.url.path == "/records" and request.method == "POST":
        if request.headers["Authorization"] == "Bearer old":
            return httpx.Response(401, text="refresh required")
        assert json.loads(request.content) == payload
        return httpx.Response(200, json=payload)
    if request.url.path.startswith("/records/"):
        assert request.url.raw_path.startswith(b"/records/a%2Fb%20c?")
        assert request.url.params["Paging.Limit"] == "3"
        assert request.headers["X-Request-Id"] == "trace-id"
        return httpx.Response(200, json=payload)
    if request.url.path == "/records":
        return httpx.Response(200, json=[payload])
    if request.url.path in ("/empty", "/mixed", "/optional-body"):
        if request.url.path == "/optional-body":
            assert request.content == b""
        return httpx.Response(204)
    if request.url.path == "/unauthorized":
        return httpx.Response(401, text="still unauthorized")
    if request.url.path == "/error":
        return httpx.Response(503, text="service offline", headers={"content-type": "text/plain"})
    raise AssertionError(f"Unexpected request: {request.url}")


def assert_replay(auth):
    replay = [request for request in requests if request[0] == "POST" and request[1] == b"/records"]
    assert len(replay) == 2
    assert replay[0][3] == replay[1][3]
    assert replay[0][2]["authorization"] == "Bearer old"
    assert replay[1][2]["authorization"] == "Bearer refreshed"
    assert auth.refreshes == 1
    assert hooks[:2] == ["Bearer old", "Bearer refreshed"]


def assert_error(error, status, body):
    assert error.response.status_code == status
    assert error.response.text == body


def run_sync():
    auth = RefreshOnce()
    with httpx.Client(
        base_url="https://example.test", transport=httpx.MockTransport(handle), auth=auth,
        headers={"X-Application": "shared"},
        event_hooks={"request": [lambda request: hooks.append(request.headers["Authorization"])]},
    ) as transport:
        client = Client(transport)
        assert client._client is transport
        assert isinstance(client.save_record(envelope), Envelope)
        assert_replay(auth)
        assert isinstance(client.get_record("a/b c", x_request_id="trace-id", paging_limit=3), Envelope)
        result = client.list_records()
        assert isinstance(result[0], Envelope) and isinstance(result[0].children[0], Child)
        assert client.empty() is None
        assert client.mixed() is None
        assert client.optional_body() is None
        try:
            client.error()
        except httpx.HTTPStatusError as error:
            assert_error(error, 503, "service offline")
        else:
            raise AssertionError("503 did not raise")
        before = len(requests)
        try:
            client.unauthorized()
        except httpx.HTTPStatusError as error:
            assert_error(error, 401, "still unauthorized")
        else:
            raise AssertionError("Repeated 401 did not raise")
        assert len(requests) - before == 2
        assert not transport.is_closed
    assert transport.is_closed


async def run_async():
    auth = RefreshOnce()
    started = asyncio.Event()
    cancelled = asyncio.Event()

    async def async_handle(request):
        if request.url.path == "/wait":
            requests.append((request.method, request.url.raw_path, dict(request.headers), request.content))
            started.set()
            try:
                await asyncio.Event().wait()
            finally:
                cancelled.set()
        return handle(request)

    async def on_request(request):
        hooks.append(request.headers["Authorization"])

    async with httpx.AsyncClient(
        base_url="https://example.test", transport=httpx.MockTransport(async_handle), auth=auth,
        headers={"X-Application": "shared"}, event_hooks={"request": [on_request]},
    ) as transport:
        client = Client(transport)
        assert client._client is transport
        assert isinstance(await client.save_record(envelope), Envelope)
        assert_replay(auth)
        assert isinstance(await client.get_record("a/b c", x_request_id="trace-id", paging_limit=3), Envelope)
        result = await client.list_records()
        assert isinstance(result[0], Envelope) and isinstance(result[0].children[0], Child)
        assert await client.empty() is None
        assert await client.mixed() is None
        assert await client.optional_body() is None
        try:
            await client.error()
        except httpx.HTTPStatusError as error:
            assert_error(error, 503, "service offline")
        else:
            raise AssertionError("503 did not raise")
        before = len(requests)
        try:
            await client.unauthorized()
        except httpx.HTTPStatusError as error:
            assert_error(error, 401, "still unauthorized")
        else:
            raise AssertionError("Repeated 401 did not raise")
        assert len(requests) - before == 2
        pending = asyncio.create_task(client.wait())
        await asyncio.wait_for(started.wait(), timeout=2)
        pending.cancel()
        try:
            await pending
        except asyncio.CancelledError:
            pass
        else:
            raise AssertionError("Task cancellation did not propagate")
        assert cancelled.is_set()
        assert len([request for request in requests if request[1] == b"/wait"]) == 1
        assert not transport.is_closed
    assert transport.is_closed


if sys.argv[1] == "async":
    asyncio.run(run_async())
else:
    run_sync()
print("generated package validation, HTTPX hooks/Auth replay, ownership, and cancellation passed")
