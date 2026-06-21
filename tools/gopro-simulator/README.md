# GoPro simulator

Small local simulator for developing the photobooth without a real GoPro.

It serves a subset of the GoPro HTTP API used by this project:

- `GET /gp/gpControl/...`
- `GET /gp/gpMediaList`
- `GET /videos/DCIM/<directory>/<file>`

It also starts a UDP listener for the GoPro keepalive packet.

## Run

From the repository root:

```powershell
python tools\gopro-simulator\simulator.py --host 127.0.0.1 --port 8080 --udp-port 8554
```

Then configure the app to use:

```text
ControlBaseUrl = http://127.0.0.1:8080
MediaBaseUrl = http://127.0.0.1:8080
KeepAliveHost = 127.0.0.1
KeepAlivePort = 8554
```

The current UWP app still hardcodes `10.5.5.9`, so the app must be made configurable before it can use this simulator directly.

## Behavior

- A shutter start command adds a new fake JPG to the media list.
- All fake JPG files return the same local sample image.
- Stop/mode commands return HTTP 200.
- UDP keepalive packets are accepted and printed.

