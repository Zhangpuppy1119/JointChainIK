using UnityEngine;

public class JointChainIK : MonoBehaviour
{
    [Header("Chain Settings")]
    [Range(3, 12)]
    public int jointCount = 6;

    public float segmentLength = 1f;
    public int maxIterations = 80;
    public float tolerance = 0.015f;

    private Transform[] joints;
    private LineRenderer chainLine;
    private Transform targetMarker;
    private Material drawingMaterial;
    private Sprite jointSprite;
    private Texture2D jointTexture;
    private Camera mainCamera;

    private Vector3 target;
    private bool dragging;
    private int selectedCount = 6;

    private readonly Vector3 rootPosition = Vector3.zero;
    private readonly Rect panelRect = new Rect(15, 15, 280, 480);

    void Start()
    {
        mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogError("Please keep a Main Camera in the scene.");
            enabled = false;
            return;
        }

        mainCamera.orthographic = true;
        mainCamera.transform.position = new Vector3(0, 0, -10);
        mainCamera.transform.rotation = Quaternion.identity;
        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = new Color(0.055f, 0.075f, 0.12f);

        drawingMaterial = new Material(Shader.Find("Sprites/Default"));
        CreateCircleSprite();

        selectedCount = Mathf.Clamp(jointCount, 3, 12);
        BuildChain();
    }

    // Generate a circular sprite without external image assets.
    void CreateCircleSprite()
    {
        const int size = 64;
        jointTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        jointTexture.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(
                    new Vector2(x + 0.5f, y + 0.5f),
                    new Vector2(size / 2f, size / 2f));

                float alpha = Mathf.Clamp01(size / 2f - 1f - distance);
                jointTexture.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }

        jointTexture.Apply();
        jointSprite = Sprite.Create(
            jointTexture,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            size);
    }

    Transform CreateMarker(string markerName, Color color, float size)
    {
        GameObject marker = new GameObject(markerName);
        marker.transform.SetParent(transform, false);
        marker.transform.localScale = Vector3.one * size;

        SpriteRenderer renderer = marker.AddComponent<SpriteRenderer>();
        renderer.sprite = jointSprite;
        renderer.color = color;
        renderer.sortingOrder = 2;

        return marker.transform;
    }

    void BuildChain()
    {
        dragging = false;

        if (joints != null)
        {
            foreach (Transform joint in joints)
            {
                if (joint != null)
                    Destroy(joint.gameObject);
            }
        }

        if (chainLine != null)
            Destroy(chainLine.gameObject);

        if (targetMarker != null)
            Destroy(targetMarker.gameObject);

        jointCount = selectedCount;
        joints = new Transform[jointCount];

        // A slightly curved initial pose avoids a straight-chain singularity.
        Vector3 position = rootPosition;
        float direction = 35f;

        for (int i = 0; i < jointCount; i++)
        {
            Color color = new Color(0.25f, 0.8f, 1f);
            float size = 0.22f;

            if (i == 0)
            {
                color = new Color(1f, 0.7f, 0.2f);
                size = 0.32f;
            }
            else if (i == jointCount - 1)
            {
                color = new Color(0.3f, 1f, 0.55f);
                size = 0.32f;
            }

            joints[i] = CreateMarker("Joint_" + i, color, size);
            joints[i].position = position;

            if (i < jointCount - 1)
            {
                float radians = direction * Mathf.Deg2Rad;
                position += new Vector3(
                    Mathf.Cos(radians),
                    Mathf.Sin(radians), 0) * segmentLength;

                direction += 12f;
            }
        }

        GameObject lineObject = new GameObject("ChainLine");
        lineObject.transform.SetParent(transform, false);
        chainLine = lineObject.AddComponent<LineRenderer>();
        chainLine.sharedMaterial = drawingMaterial;
        chainLine.useWorldSpace = true;
        chainLine.positionCount = jointCount;
        chainLine.startWidth = 0.09f;
        chainLine.endWidth = 0.09f;
        chainLine.startColor = new Color(0.2f, 0.7f, 1f);
        chainLine.endColor = new Color(0.3f, 1f, 0.65f);
        chainLine.sortingOrder = 0;
        chainLine.numCapVertices = 6;

        target = joints[jointCount - 1].position;
        targetMarker = CreateMarker(
            "Target", new Color(1f, 0.3f, 0.4f), 0.16f);

        targetMarker.GetComponent<SpriteRenderer>().sortingOrder = 3;

        // Reserve room on the left for the information panel.
        float reach = (jointCount - 1) * segmentLength;
        float aspect = Mathf.Max(mainCamera.aspect, 0.5f);
        mainCamera.orthographicSize =
            Mathf.Max(4f, reach * 1.25f, reach * 1.7f / aspect);

        RefreshVisuals();
    }

    void Update()
    {
        if (joints == null)
            return;

        Vector3 mouseWorld = mainCamera.ScreenToWorldPoint(
            new Vector3(Input.mousePosition.x, Input.mousePosition.y, 10f));
        mouseWorld.z = 0;

        Vector2 guiMouse = new Vector2(
            Input.mousePosition.x,
            Screen.height - Input.mousePosition.y);

        // Dragging starts only when the end joint is clicked.
        if (Input.GetMouseButtonDown(0) && !panelRect.Contains(guiMouse))
        {
            Vector3 endScreen = mainCamera.WorldToScreenPoint(
                joints[jointCount - 1].position);

            dragging = Vector2.Distance(
                new Vector2(endScreen.x, endScreen.y),
                new Vector2(Input.mousePosition.x, Input.mousePosition.y)) < 28f;
        }

        if (Input.GetMouseButtonUp(0))
            dragging = false;

        if (dragging && Input.GetMouseButton(0))
            target = mouseWorld;

        SolveCCD();
        RefreshVisuals();
    }

    // CCD: rotate each joint and all following joints toward the target.
    void SolveCCD()
    {
        int last = jointCount - 1;
        float reach = last * segmentLength;
        Vector3 rootToTarget = target - rootPosition;

        // Unreachable target: fully extend while preserving segment lengths.
        if (rootToTarget.magnitude >= reach)
        {
            Vector3 direction = rootToTarget.normalized;

            for (int i = 0; i < jointCount; i++)
                joints[i].position = rootPosition + direction * segmentLength * i;

            return;
        }

        // If perfectly straight, introduce a tiny bend before iteration.
        Vector3 baseDirection = joints[1].position - rootPosition;
        bool straight = true;

        for (int i = 1; i < last; i++)
        {
            Vector3 segment = joints[i + 1].position - joints[i].position;
            if (Vector3.Cross(baseDirection.normalized,
                              segment.normalized).sqrMagnitude > 0.000001f)
            {
                straight = false;
                break;
            }
        }

        if (straight &&
            Vector3.Distance(joints[last].position, target) > tolerance)
        {
            RotateFollowingJoints(last - 1, 2f);
        }

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            if (Vector3.Distance(joints[last].position, target) <= tolerance)
                break;

            for (int i = last - 1; i >= 0; i--)
            {
                Vector3 toEnd = joints[last].position - joints[i].position;
                Vector3 toTarget = target - joints[i].position;

                if (toEnd.sqrMagnitude < 0.0000001f ||
                    toTarget.sqrMagnitude < 0.0000001f)
                    continue;

                float angle = Vector2.SignedAngle(
                    new Vector2(toEnd.x, toEnd.y),
                    new Vector2(toTarget.x, toTarget.y));

                RotateFollowingJoints(i, angle);

                if (Vector3.Distance(joints[last].position, target) <= tolerance)
                    break;
            }
        }
    }

    void RotateFollowingJoints(int pivotIndex, float angle)
    {
        Vector3 pivot = joints[pivotIndex].position;
        Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.forward);

        for (int i = pivotIndex + 1; i < jointCount; i++)
        {
            joints[i].position =
                pivot + rotation * (joints[i].position - pivot);
        }
    }

    void RefreshVisuals()
    {
        for (int i = 0; i < jointCount; i++)
            chainLine.SetPosition(i, joints[i].position);

        targetMarker.position = target;

        // Assign world rotations corresponding to the solved segment directions.
        for (int i = 0; i < jointCount - 1; i++)
        {
            Vector3 direction = joints[i + 1].position - joints[i].position;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            joints[i].rotation = Quaternion.Euler(0, 0, angle);
        }

        joints[jointCount - 1].rotation = joints[jointCount - 2].rotation;
    }

    void OnGUI()
    {
        if (joints == null)
            return;

        GUILayout.BeginArea(panelRect, GUI.skin.box);
        GUILayout.Label("CCD INVERSE KINEMATICS");
        GUILayout.Space(8);
        GUILayout.Label("Joint count (including root and end)");

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("-", GUILayout.Width(40)))
            selectedCount = Mathf.Max(3, selectedCount - 1);

        GUILayout.Label(selectedCount.ToString(), GUILayout.Width(50));

        if (GUILayout.Button("+", GUILayout.Width(40)))
            selectedCount = Mathf.Min(12, selectedCount + 1);

        GUILayout.EndHorizontal();

        if (GUILayout.Button("Generate / Reset"))
            BuildChain();

        GUILayout.Space(8);
        GUILayout.Label("Drag the GREEN end joint.");
        GUILayout.Label("ORANGE: fixed root");
        GUILayout.Label("RED: mouse target");
        GUILayout.Label("Algorithm: CCD");
        GUILayout.Label("Joints: " + jointCount +
                        "    Segments: " + (jointCount - 1));

        float error = Vector3.Distance(
            joints[jointCount - 1].position, target);

        float reach = (jointCount - 1) * segmentLength;
        bool reachable = Vector3.Distance(rootPosition, target) <= reach;

        GUILayout.Label("Target: " + (reachable ? "Reachable" : "Out of reach"));
        GUILayout.Label("End error: " + error.ToString("F3"));
        GUILayout.Label("Root position: (0, 0)");
        GUILayout.Space(6);
        GUILayout.Label("Joint angles (relative, degrees)");

        float previousAngle = 0;

        for (int i = 0; i < jointCount - 1; i++)
        {
            Vector3 direction = joints[i + 1].position - joints[i].position;
            float worldAngle =
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            float relativeAngle = Mathf.DeltaAngle(previousAngle, worldAngle);

            GUILayout.Label("J" + i + ": " + relativeAngle.ToString("F1"));
            previousAngle = worldAngle;
        }

        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        if (drawingMaterial != null)
            Destroy(drawingMaterial);

        if (jointSprite != null)
            Destroy(jointSprite);

        if (jointTexture != null)
            Destroy(jointTexture);
    }
}