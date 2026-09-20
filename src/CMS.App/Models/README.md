# AI models

Drop the ONNX model files into this folder. They are **not** bundled with the
source because model weights are large and licensed separately from this code.

The application starts and runs normally with any or all of them missing — the
affected screen simply reports "model not loaded" and everything else keeps
working. This is deliberate: a camera management system must not refuse to show
video because an analytics model is absent.

| File | Used by | What it must be |
|---|---|---|
| `yolov26n.onnx` | Object Detection | A YOLO object-detection export, 3-channel RGB input in NCHW order. Both head layouts are handled automatically: the anchor-free `[1, 4 + classes, boxes]` used by YOLOv26/v8/v11, and the older `[1, boxes, 5 + classes]` that carries an objectness score. Class order is assumed to be the 80 COCO classes. |
| `face_detection.onnx` | Face Recognition | A single-class face detector. Either a YOLO-face export (`[1, 5+, N]` either way round) or a YuNet export (`[N, 15]` rows of box, landmarks, score). |
| `face_recognition.onnx` | Face Recognition | A face-embedding model, typically ArcFace-style: 112×112 input, a 512-float output vector. Inputs are scaled as `(pixel − 127.5) / 128`. |

The exact file names are configurable under **Settings → AI Models**, so a model
kept elsewhere on disk can be pointed at with an absolute path instead.

## Sizing and input shape

Input width and height are read from the model itself at load time, so a 320×320
or 1280×1280 export works without any code change. Letterbox padding preserves
the source aspect ratio, which is what keeps the drawn boxes aligned with the
video.

## Model variants

The Object Detection screen offers three variants. Selecting one only changes
which file is loaded:

- `YOLOv26n (Fast & Lightweight)` → `yolov26n.onnx`
- `YOLOv26s (Balanced)` → `yolov26s.onnx`
- `YOLOv26m (Accurate)` → `yolov26m.onnx`

## Verifying a model loaded

**System Information → AI Models** lists the resolved path and input size for
each model, and **Settings → AI Models → Reload Models** reports the load result
for all three without restarting.
