#!/usr/bin/env python3
"""Verify the running web image can validate a schema and generate a ZIP."""

import base64
import io
import json
import sys
import urllib.request
import zipfile


def request(base_url, path, payload=None):
    """Read an endpoint, optionally posting a JSON generation request."""
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    headers = {} if payload is None else {"Content-Type": "application/json"}
    with urllib.request.urlopen(
        urllib.request.Request(base_url + path, data=data, headers=headers), timeout=45
    ) as response:
        return response.read()


def main():
    """Exercise the production server through its HTTP API."""
    base_url = (sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:18080").rstrip("/")
    assert request(base_url, "/healthz") == b"Healthy"
    languages = json.loads(request(base_url, "/api/languages"))
    assert any(language["languageId"] == "csharp" for language in languages), languages
    homepage = request(base_url, "/").decode("utf-8")
    assert "_framework/blazor.web.js" in homepage

    schema = {
        "openapi": "3.0.3",
        "info": {"title": "Web container smoke", "version": "1.0.0"},
        "paths": {},
        "components": {
            "schemas": {
                "SmokeModel": {
                    "type": "object",
                    "properties": {"name": {"type": "string"}},
                }
            }
        },
    }
    payload = {
        "schemaFileName": "smoke.json",
        "schemaContentBase64": base64.b64encode(json.dumps(schema).encode()).decode(),
        "languageId": "csharp",
        "namespace": "ValIO.Smoke",
    }
    validation = json.loads(request(base_url, "/api/validate", payload))
    assert validation["isValid"] and validation["componentSchemaCount"] == 1, validation
    with zipfile.ZipFile(io.BytesIO(request(base_url, "/api/generate", payload))) as archive:
        generated = [name for name in archive.namelist() if name.endswith(".cs")]
        assert generated, archive.namelist()
        assert any(b"SmokeModel" in archive.read(name) for name in generated)
    print(f"Web smoke passed: {len(languages)} languages, homepage, schema validation and C# ZIP.")


if __name__ == "__main__":
    main()
