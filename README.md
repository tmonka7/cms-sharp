# Camera Management System

A Windows surveillance client built as a **.NET Framework 4.7 WinForms**
application. It manages ONVIF/RTSP cameras, records to local disk, and runs
YOLO object detection and face recognition on the live streams.

Everything runs offline. There is no cloud account, no licence server and no
telemetry; the only network traffic is to the cameras themselves, on the local
network.

## Requirements

- Windows 10 or 11, **x64**
- .NET Framework 4.7 or later (present on any current Windows install)
- Visual Studio 2022 or the .NET SDK, to build

The native dependencies — OpenCV, FFmpeg, ONNX Runtime and SQLite — come from
NuGet and are copied next to the executable. They are 64-bit only, which is why
the projects are pinned to `x64`.

## Build and run

```
dotnet build -c Release
src\CMS.App\bin\Release\net47\CameraManagementSystem.exe
```

The first launch creates the local database and a default administrator:

```
username: admin
password: admin1234
```

Change it from **User Management** before putting the system into service.

## Layout

```
src/
  CMS.Core/            no UI; usable from a service or a test harness
    Models/            domain types
    Data/              SQLite schema and repositories
    Onvif/             WS-Discovery, SOAP transport, Device/Media/PTZ clients
    Streaming/         RTSP decoding, recording playback
    Ai/                YOLO detector, face detector, face embedder
    Services/          analytics engine, auth, recording, system monitor
  CMS.App/             WinForms client
    Theme.cs           colours, fonts and drawing helpers
    Controls/          owner-drawn controls (video surface, tables, PTZ pad…)
    Forms/             splash, login, shell, dialogs
    Pages/             one file per screen
```

## What the screens do

| Screen | Purpose |
|---|---|
| Dashboard | Fleet counters, live thumbnail wall, recent events, machine load |
| Live View | Single or tiled video with detection and recognition overlays; snapshot, record, PTZ, full screen |
| Playback | Recorded footage with a 24-hour timeline, event markers and speed control |
| Camera List | Compact wall of every channel with connection state |
| Device Management | Add, edit and remove cameras; ONVIF network scan |
| PTZ Control | Pan/tilt pad, zoom, focus, iris, presets, auto scan / pattern / cruise |
| Object Detection | Class filter, confidence threshold, live preview and per-class tallies |
| Face Recognition | Live identity boxes, latest match, recent match history |
| Face Database | Enrolled identities; enrol from a photo, a camera frame, or a folder |
| Event Log | Filterable, paged history with stored snapshots and CSV export |
| User Management | Local accounts, roles and per-screen permissions |
| System Information | Build identity, CPU/memory/disk gauges, host network, model status |
| Settings | General, network, storage and AI model configuration |

## How the pieces fit together

**ONVIF** is implemented directly against the wire format rather than through
generated WSDL proxies, so there is no code-generation step and no service
reference to refresh. `OnvifDiscovery` sends a WS-Discovery probe over UDP
multicast from every local interface; `OnvifSoapClient` signs each SOAP 1.2
request with a WS-Security UsernameToken digest, which is what essentially every
camera expects. Device, media and PTZ operations sit on top of that.

**Video** is decoded by OpenCV's FFmpeg backend, one background thread per
camera, with automatic reconnection. RTSP is forced over TCP because UDP tearing
is the most common cause of "the camera looks broken" reports.

**Analytics** run on sampled frames, not every frame. Inference is far slower
than decoding, so each camera holds at most one analysis in flight and skips
rather than queues — a backlog would only ever draw stale boxes. Detected
objects and recognised faces are written to the event log with a per-class
cooldown so a person standing in view does not flood it.

**Storage** is a single SQLite file holding cameras, users, faces, events,
recordings and settings, plus MP4 segments on disk. Backing up that one file and
the media folder backs up the whole installation.

## AI models

Model weights are not committed. Drop the ONNX files into
`src/CMS.App/Models/` — see [the note there](src/CMS.App/Models/README.md) for
the expected files and tensor shapes.

Every screen works with the models absent; the affected panel reports "model not
loaded" instead. Live video, recording, playback, PTZ and device management do
not depend on them.

## Notes for a production deployment

- Replace the default administrator password on first run.
- Camera passwords are stored in the local database in recoverable form, because
  the application has to present them to the device on every connect. Protect
  the database file accordingly (`%LOCALAPPDATA%\CameraManagementSystem\cms.db`).
- HTTPS certificate validation is disabled for ONVIF, since cameras ship
  self-signed certificates. That is safe on an isolated camera VLAN and should be
  reconsidered if the cameras are routable.
