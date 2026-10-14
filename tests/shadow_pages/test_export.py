"""Pages regression tests; no .NET runtime or external service is needed."""
from copy import deepcopy
import hashlib
from html.parser import HTMLParser
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import MagicMock, patch
from urllib.parse import parse_qs, urlsplit
import zlib

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("shadow_studio_pages", ROOT / "tools/ci/shadow_studio_pages.py")
pages = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(pages)
COMMIT = "a" * 40


def png() -> bytes:
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    header = struct.pack(">IIBBBBB", pages.WIDTH, pages.HEIGHT, 8, 0, 0, 0, 0)
    pixels = zlib.compress((b"\0" + b"\xff" * pages.WIDTH) * pages.HEIGHT)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", pixels) + chunk(b"IEND", b"")


PNG = png()
SVG = b'<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="800"><path d="M0 0L10 10"/></svg>'


class FakeStudio:
    def __init__(self, layouts=None):
        self.state = {"documentId": "document", "workspaceId": "live", "viewVersion": 1,
                      "revision": 3, "layout": "Model", "occurrences": 5, "diagnostics": [],
                      "layouts": layouts or ["Model", "Paper viewports"], "backends": ["skia", "svg"]}
        self.render_calls = []

    def json(self, path, state=None, payload=None):
        if path == "/api/layout":
            if state != self.state or payload["revision"] != self.state["revision"]:
                raise AssertionError("Exporter used stale Studio state")
            self.state["layout"] = payload["name"]
            self.state["viewVersion"] += 1
        elif path != "/api/state":
            raise AssertionError(path)
        return deepcopy(self.state)

    def request(self, path, state=None):
        if state != self.state:
            raise AssertionError("Exporter omitted the latest document/workspace/view receipt")
        query = parse_qs(urlsplit(path).query)
        if query["width"] != [str(pages.WIDTH)] or query["height"] != [str(pages.HEIGHT)]:
            raise AssertionError(query)
        backend = query["backend"][0]
        self.render_calls.append((self.state["layout"], backend, state["viewVersion"]))
        return (PNG, "image/png") if backend == "skia" else (SVG, "image/svg+xml")


class Links(HTMLParser):
    def __init__(self):
        super().__init__()
        self.local = []
        self.scripts = []

    def handle_starttag(self, tag, attrs):
        if tag == "script":
            self.scripts.append(attrs)
        for key, value in attrs:
            if key in {"href", "src"} and value and not urlsplit(value).scheme and not value.startswith("#"):
                self.local.append(value)


class ExportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name) / "site"

    def export(self, client=None):
        return pages.export_site(client or FakeStudio(), self.output, "wieslawsoltes/netDxf", COMMIT)

    def test_all_layouts_both_renderers_and_source_manifest(self):
        client = FakeStudio()
        manifest = self.export(client)
        self.assertEqual(client.render_calls, [
            ("Model", "skia", 2), ("Model", "svg", 2),
            ("Paper viewports", "skia", 3), ("Paper viewports", "svg", 3)])
        self.assertEqual(manifest["commit"], COMMIT)
        self.assertEqual(manifest["mode"], "read-only")
        self.assertEqual(len(manifest["assets"]), 6)
        self.assertEqual(manifest, json.loads((self.output / "manifest.json").read_text()))
        for name, expected in manifest["assets"].items():
            body = (self.output / name).read_bytes()
            self.assertEqual(expected["bytes"], len(body))
            self.assertEqual(expected["sha256"], hashlib.sha256(body).hexdigest())
        self.assertTrue((self.output / ".nojekyll").is_file())
        self.assertFalse(any(self.output.rglob("*.dll")))

    def test_pages_is_scriptless_and_project_subpath_safe(self):
        self.export()
        html = (self.output / "index.html").read_text()
        links = Links()
        links.feed(html)
        self.assertFalse(links.scripts)
        self.assertNotIn("/api/", html)
        self.assertIn("Read-only GitHub Pages preview", html)
        self.assertIn("require the local host", html)
        self.assertGreater(len(links.local), 2)
        for value in links.local:
            self.assertFalse(value.startswith("/"), value)
            self.assertTrue((self.output / value).is_file(), value)

    def test_untrusted_layout_names_are_escaped_and_not_paths(self):
        self.export(FakeStudio(['../../unsafe/<script>alert("x")</script>', "A/B", "A B"]))
        html = (self.output / "index.html").read_text()
        self.assertIn("&lt;script&gt;", html)
        self.assertNotIn("<script>", html)
        self.assertEqual({p.name for p in (self.output / "layouts").glob("*.png")},
                         {"000.png", "001.png", "002.png"})

    def test_failed_render_never_publishes_partial_site(self):
        client = FakeStudio()
        client.request = lambda *args: (b"error page", "text/html")
        with self.assertRaises(ValueError):
            self.export(client)
        self.assertFalse(self.output.exists())
        self.assertFalse(list(self.output.parent.glob(".shadow-pages-*")))

    def test_existing_output_is_never_overwritten(self):
        self.output.mkdir()
        marker = self.output / "keep.txt"
        marker.write_text("keep")
        with self.assertRaises(FileExistsError):
            self.export()
        self.assertEqual(marker.read_text(), "keep")

    def test_invalid_layout_lists_are_rejected(self):
        for layouts in ([], [""], ["Model", "Model"], [123], "Model", [str(i) for i in range(101)]):
            with self.subTest(layouts=layouts):
                client = FakeStudio()
                client.state["layouts"] = layouts
                with self.assertRaises(ValueError):
                    self.export(client)
                self.assertFalse(self.output.exists())

    def test_both_backends_are_required(self):
        client = FakeStudio()
        client.state["backends"] = ["svg"]
        with self.assertRaises(ValueError):
            self.export(client)

    def test_wrong_layout_receipt_is_rejected(self):
        client = FakeStudio()
        original = client.json
        def changed(path, state=None, payload=None):
            result = original(path, state, payload)
            if path == "/api/layout":
                result["workspaceId"] = "another-workspace"
            return result
        client.json = changed
        with self.assertRaises(ValueError):
            self.export(client)
        self.assertFalse(self.output.exists())

    def test_repository_and_commit_are_validated(self):
        for repository, commit in (("https://example.com", COMMIT), ("owner/repo", "HEAD"),
                                   ("owner/repo\"", COMMIT), ("owner/repo", "")):
            with self.subTest(repository=repository, commit=commit):
                with self.assertRaises(ValueError):
                    pages.export_site(FakeStudio(), self.output, repository, commit)


class ClientTests(unittest.TestCase):
    def test_loopback_only(self):
        for origin in ("https://example.com", "http://example.com", "http://127.0.0.1/api",
                       "http://u:p@127.0.0.1", "http://127.0.0.1?q=1"):
            with self.subTest(origin=origin):
                with self.assertRaises(ValueError):
                    pages.StudioClient(origin)
        pages.StudioClient("http://127.0.0.1:5080")
        pages.StudioClient("http://[::1]:5080")

    def test_post_has_all_request_identity_headers(self):
        client = pages.StudioClient("http://127.0.0.1:5080")
        response = MagicMock()
        response.read.return_value = b'{"ok": true}'
        response.headers.get_content_type.return_value = "application/json"
        client.opener = MagicMock()
        client.opener.open.return_value.__enter__.return_value = response
        state = {"documentId": "id", "workspaceId": "live", "viewVersion": 7}
        self.assertEqual(client.json("/api/layout", state, {"revision": 1, "name": "Model"}), {"ok": True})
        request = client.opener.open.call_args.args[0]
        headers = {name.lower(): value for name, value in request.header_items()}
        self.assertEqual(request.get_method(), "POST")
        self.assertEqual(headers["x-shadow-studio"], "1")
        self.assertEqual(headers["x-shadow-document"], "id")
        self.assertEqual(headers["x-shadow-workspace"], "live")
        self.assertEqual(headers["x-shadow-view"], "7")
        self.assertEqual(headers["content-type"], "application/json")
        self.assertEqual(json.loads(request.data)["name"], "Model")

    def test_json_requires_object_and_content_type(self):
        client = pages.StudioClient("http://127.0.0.1:5080")
        for body, mime in ((b"[]", "application/json"), (b"{}", "text/html")):
            with self.subTest(body=body, mime=mime):
                client.request = lambda *args: (body, mime)
                with self.assertRaises(ValueError):
                    client.json("/api/state")

    def test_redirects_and_non_api_paths_are_rejected(self):
        with self.assertRaises(RuntimeError):
            pages.NoRedirect().redirect_request(None, None, 302, "", {}, "https://example.com")
        with self.assertRaises(ValueError):
            pages.StudioClient("http://127.0.0.1:5080").request("//example.com")

    def test_server_terminates_even_when_export_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            published = Path(directory)
            (published / "netDxf.Shadow.Studio.dll").touch()
            process = MagicMock()
            process.poll.return_value = None
            with patch.object(pages.subprocess, "Popen", return_value=process), \
                 patch.object(pages.StudioClient, "request", return_value=(b'{"status":"ok"}', "application/json")):
                with self.assertRaisesRegex(RuntimeError, "export failed"):
                    with pages.studio_server(published, published / "host.log"):
                        raise RuntimeError("export failed")
            process.terminate.assert_called_once()
            process.wait.assert_called_once_with(timeout=10)

    def test_missing_published_host_fails_before_start(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(pages.subprocess, "Popen") as start:
            with self.assertRaises(FileNotFoundError):
                with pages.studio_server(Path(directory), Path(directory) / "host.log"):
                    pass
            start.assert_not_called()


class RenderTests(unittest.TestCase):
    def test_valid_images(self):
        pages.validate_render(PNG, "image/png", "skia")
        pages.validate_render(SVG, "image/svg+xml", "svg")

    def test_bad_mime_signature_dimensions_and_svg_are_rejected(self):
        for body, mime, backend in ((PNG, "text/html", "skia"), (b"bad", "image/png", "skia"),
                                   (PNG[:16] + b"\0" * 8 + PNG[24:], "image/png", "skia"),
                                   (b"<html/>", "image/svg+xml", "svg"),
                                   (SVG, "text/html", "svg"),
                                   (b'<!DOCTYPE svg>' + SVG, "image/svg+xml", "svg"),
                                   (PNG, "image/png", "other")):
            with self.subTest(mime=mime, backend=backend):
                with self.assertRaises(ValueError):
                    pages.validate_render(body, mime, backend)


if __name__ == "__main__":
    unittest.main()
