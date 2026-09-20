namespace CMS.Core.Ai;

/// <summary>
/// The 80 COCO classes YOLO models are trained on. The Object Detection screen
/// shows a curated subset and folds everything else into "others".
/// </summary>
public static class CocoLabels
{
    public static readonly string[] Names =
    {
        "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat",
        "traffic light", "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat",
        "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe", "backpack",
        "umbrella", "handbag", "tie", "suitcase", "frisbee", "skis", "snowboard", "sports ball",
        "kite", "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket",
        "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
        "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair",
        "couch", "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse",
        "remote", "keyboard", "cell phone", "microwave", "oven", "toaster", "sink",
        "refrigerator", "book", "clock", "vase", "scissors", "teddy bear", "hair drier",
        "toothbrush"
    };

    /// <summary>The classes with their own row in the detection count panel.</summary>
    public static readonly string[] Featured =
    {
        "person", "car", "bicycle", "motorcycle", "bus", "truck", "dog", "cat"
    };

    public static string Get(int classId)
        => classId >= 0 && classId < Names.Length ? Names[classId] : "class_" + classId;

    public static bool IsFeatured(string label)
        => Featured.Contains(label, StringComparer.OrdinalIgnoreCase);
}
