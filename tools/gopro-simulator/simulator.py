#!/usr/bin/env python3
import argparse
import json
import mimetypes
import socket
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse, parse_qs


class SimulatorState:
    def __init__(self, sample_image: Path):
        self.sample_image = sample_image
        self.directory = "100GOPRO"
        self.files = ["GOPR0001.JPG", "GOPR0002.JPG", "GOPR0003.JPG"]
        self.recording = False
        self.mode = "photo"
        self.lock = threading.Lock()

    def add_photo(self):
        with self.lock:
            next_number = len([f for f in self.files if f.upper().endswith(".JPG")]) + 1
            name = f"GOPR{next_number:04d}.JPG"
            self.files.append(name)
            return name

    def media_list(self):
        with self.lock:
            return {
                "id": "simulated-gopro",
                "media": [
                    {
                        "d": self.directory,
                        "fs": [
                            {
                                "n": name,
                                "mode": "photo" if name.upper().endswith(".JPG") else "video",
                                "ls": "0",
                                "s": str(self.sample_image.stat().st_size),
                            }
                            for name in self.files
                        ],
                    }
                ],
            }


def make_handler(state: SimulatorState):
    class GoProSimulatorHandler(BaseHTTPRequestHandler):
        server_version = "GoProSimulator/1.0"

        def log_message(self, fmt, *args):
            print("%s - %s" % (self.address_string(), fmt % args))

        def do_GET(self):
            parsed = urlparse(self.path)
            path = parsed.path
            query = parsed.query

            if path.startswith("/gp/gpControl/"):
                self.handle_control(path, query)
                return

            if path == "/gp/gpMediaList":
                self.send_json(state.media_list())
                return

            prefix = f"/videos/DCIM/{state.directory}/"
            if path.startswith(prefix):
                requested = path[len(prefix):]
                if requested in state.files:
                    self.send_file(state.sample_image)
                else:
                    self.send_error(404, "Unknown simulated media")
                return

            self.send_error(404, "Unknown simulated GoPro endpoint")

        def handle_control(self, path, query):
            qs = parse_qs(query)
            if "command/shutter" in path:
                pressed = qs.get("p", ["0"])[0]
                if pressed == "1":
                    if state.mode == "video":
                        state.recording = True
                    else:
                        created = state.add_photo()
                        print(f"simulated capture: {created}")
                else:
                    state.recording = False
            elif "command/sub_mode" in path:
                # Parse the actual "mode" param. GoPro photo modes use mode=1, video uses mode=0.
                # (The previous naive `"mode=0" in query` test wrongly matched "sub_mode=0".)
                mode = qs.get("mode", ["1"])[0]
                state.mode = "video" if mode == "0" else "photo"

            self.send_json({"status": "ok", "mode": state.mode, "recording": state.recording})

        def send_json(self, payload):
            body = json.dumps(payload).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def send_file(self, path: Path):
            body = path.read_bytes()
            content_type = mimetypes.guess_type(str(path))[0] or "application/octet-stream"
            self.send_response(200)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

    return GoProSimulatorHandler


def run_udp_listener(host: str, port: int):
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        sock.bind((host, port))
        print(f"UDP keepalive listener on {host}:{port}")
        while True:
            data, address = sock.recvfrom(4096)
            print(f"keepalive from {address}: {data.decode('utf-8', errors='replace').strip()}")


def main():
    repo_root = Path(__file__).resolve().parents[2]
    default_sample = repo_root / "CS" / "Assets" / "background.jpg"

    parser = argparse.ArgumentParser(description="Simulate the subset of the GoPro API used by PushButton.")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8080)
    parser.add_argument("--udp-port", type=int, default=8554)
    parser.add_argument("--sample-image", type=Path, default=default_sample)
    args = parser.parse_args()

    sample_image = args.sample_image.resolve()
    if not sample_image.exists():
        raise SystemExit(f"Sample image not found: {sample_image}")

    state = SimulatorState(sample_image)
    udp_thread = threading.Thread(target=run_udp_listener, args=(args.host, args.udp_port), daemon=True)
    udp_thread.start()

    server = ThreadingHTTPServer((args.host, args.port), make_handler(state))
    print(f"HTTP GoPro simulator on http://{args.host}:{args.port}")
    print(f"Serving sample image: {sample_image}")
    server.serve_forever()


if __name__ == "__main__":
    main()
