# AI models

Drop the ONNX model files into this folder. They are **not** committed because
model weights are large and licensed separately from this code.

The application starts and runs normally with any or all of them missing — the
affected screen simply reports "model not loaded" and everything else keeps
working. This is deliberate: a camera management system must not refuse to show
video because an analytics model is absent.

| File | Used by | What it must be |
|---|---|---|
| `yolov26n.onnx` | Object Detection | A YOLO object-detection export, 3-channel RGB input in NCHW order. Both head layouts are handled automatically: the anchor-free `[1, 4 + classes, boxes]` used by YOLOv26/v8/v11, and the older `[1, boxes, 5 + classes]` that carries an objectness score. Class order is assumed to be the 80 COCO classes. |
| `face_detection.onnx` | Face Recognition | A face detector in one of the three layouts listed below. |
| `face_recognition.onnx` | Face Recognition | A face-embedding model. The embedding width and input size are read from the file. |

The exact file names are configurable under **Settings → AI Models**, so a model
kept elsewhere on disk can be pointed at with an absolute path instead.

## The pairing this was tested against

Both files come from [opencv_zoo](https://github.com/opencv/opencv_zoo) and are
Apache-2.0, so they can ship with a commercial install:

```
Models\face_detection.onnx    <- face_detection_yunet_2023mar.onnx     (~230 KB)
Models\face_recognition.onnx  <- face_recognition_sface_2021dec.onnx   (~37 MB)
```

Measured on three identities, two samples each: same-person cosine stays at or
above 0.83 and cross-person peaks at 0.31, which is what the default match
threshold is set between.

InsightFace's `buffalo_l` is the other obvious candidate and is **not**
recommended here: its weights are licensed for non-commercial research only, and
its detector (`det_10g.onnx`) is an SCRFD export, which is not one of the
layouts below.

## Face detector layouts

The head is identified at load time, because it decides how the raw tensors are
read. A model whose layout is not recognised is **refused** at load with a
message naming its outputs, rather than loading and then finding nobody.

| Layout | Recognised by | Notes |
|---|---|---|
| YuNet | twelve outputs, `cls_`/`obj_`/`bbox_`/`kps_` per stride 8/16/32 | Decoded per stride. Supplies the five landmarks. |
| YOLO-face | one output, `[1, 4+, N]` or `[1, N, 5+]` | No landmarks. |
| Row list | one output, `[N, 15]` | Already-decoded box, landmarks and score. |

## Pixel conventions

Getting this wrong does not throw. The model still returns a unit-length vector,
but every face lands in roughly the same direction and unrelated crops score as
matches — which looks like a badly tuned threshold rather than a bug.

- **YuNet** is fed BGR at raw 0..255, the OpenCV blob defaults.
- **YOLO-face** is fed RGB scaled to 0..1.
- **The embedder** picks its convention from the embedding width: a 128-float
  output is SFace and gets BGR at raw 0..255; anything else is treated as an
  ArcFace export and gets RGB at `(pixel − 127.5) / 128`. Override it on
  `FaceEmbedder.InputConvention` if a model needs the third option, RGB at
  0..1.

## Match threshold

`Settings → AI Models → Face match threshold` is on a 0..1 scale that is cosine
similarity remapped as `(cosine + 1) / 2`. The default of 0.68 is the 0.363
cosine OpenCV publishes for SFace. Published ArcFace thresholds are quoted as
raw cosine and need the same remap before they are entered here.

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

**System Information → AI Models** lists the resolved path, the detected head
layout, the embedding width and the chosen pixel convention.
**Settings → AI Models → Reload Models** reports the same without restarting,
including the reason when a model was refused.
