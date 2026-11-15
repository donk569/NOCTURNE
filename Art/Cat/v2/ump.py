"""Minimal Unity MCP (streamable HTTP) client.

Usage:
  python ump.py list
  python ump.py call <tool_name> '<json args>'
  python ump.py py "<C# code>"      # convenience: execute C# in the editor
"""
import json
import sys
import os

import requests

URL = os.environ.get("UNITY_MCP_URL", "http://127.0.0.1:8080/mcp")
STATE = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".ump_session")

_id = [0]


class Mcp:
    def __init__(self):
        # NOTE: must NOT reuse a keep-alive Session -- the server binds sessions per
        # connection and reuse makes later calls 404.
        self.session_id = None
        self._init()

    def _post(self, payload):
        headers = {
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
        }
        if self.session_id:
            headers["mcp-session-id"] = self.session_id
        r = requests.post(URL, json=payload, headers=headers, timeout=600)
        sid = r.headers.get("mcp-session-id") or r.headers.get("Mcp-Session-Id")
        if sid:
            self.session_id = sid
        if r.status_code >= 400:
            raise RuntimeError("HTTP %s: %s" % (r.status_code, r.text[:1500]))
        txt = r.text.strip()
        if not txt:
            return None
        if txt.startswith("event:") or txt.startswith("data:"):
            # SSE framing: take last data line
            data = []
            for line in txt.splitlines():
                if line.startswith("data:"):
                    data.append(line[5:].strip())
            txt = data[-1] if data else "{}"
        return json.loads(txt)

    def _init(self):
        res = self._post({
            "jsonrpc": "2.0",
            "id": self._next(),
            "method": "initialize",
            "params": {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {"name": "nocturne-cat-pipeline", "version": "1.0"},
            },
        })
        # NOTE: do NOT send notifications/initialized -- this server answers 404 and
        # invalidates the session, making every later call fail with 404.
        return res

    def _next(self):
        _id[0] += 1
        return _id[0]

    def list_tools(self):
        res = self._post({"jsonrpc": "2.0", "id": self._next(), "method": "tools/list", "params": {}})
        return res

    def call(self, name, args):
        res = self._post({
            "jsonrpc": "2.0",
            "id": self._next(),
            "method": "tools/call",
            "params": {"name": name, "arguments": args},
        })
        return res


def _text(res):
    """Flatten an MCP tools/call result into text."""
    if res is None:
        return ""
    if "error" in res:
        return "MCP ERROR: " + json.dumps(res["error"], ensure_ascii=False)
    r = res.get("result", {})
    parts = []
    for c in r.get("content", []) or []:
        if c.get("type") == "text":
            parts.append(c.get("text", ""))
        else:
            parts.append(json.dumps(c, ensure_ascii=False))
    if not parts and "structuredContent" in r:
        parts.append(json.dumps(r["structuredContent"], ensure_ascii=False))
    return "\n".join(parts)


if __name__ == "__main__":
    m = Mcp()
    if len(sys.argv) < 2 or sys.argv[1] == "list":
        res = m.list_tools()
        for t in res.get("result", {}).get("tools", []):
            print("-", t["name"], "::", (t.get("description") or "")[:90].replace("\n", " "))
    elif sys.argv[1] == "call":
        print(_text(m.call(sys.argv[2], json.loads(sys.argv[3]) if len(sys.argv) > 3 else {})))
    elif sys.argv[1] == "py":
        code = sys.argv[2]
        res = m.call("execute_code", {"action": "execute", "code": code, "compiler": "auto"})
        print(_text(res))
    elif sys.argv[1] == "raw":
        print(json.dumps(m.call(sys.argv[2], json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}),
                         ensure_ascii=False)[:4000])
