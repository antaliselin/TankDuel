using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class TankWallVisibility : MonoBehaviour
{
    [SerializeField] private BoxCollider[] walls = Array.Empty<BoxCollider>();
    [SerializeField] private Vector3 hullHalfExtents = new Vector3(1.55f, 0.8f, 2.95f);
    [SerializeField, Min(0f)] private float hullCenterHeight = 0.95f;

    private const float WallClearance = 0.035f;
    private const float CornerOffset = 0.0003f;
    private const float FogGroundOffset = 0.10f;

    private readonly Collider[] overlaps = new Collider[16];
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private readonly List<float> angles = new List<float>();
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<int> triangles = new List<int>();
    private Mesh fogMesh;
    private Vector3 lastEye;
    private bool hasEye;

    private void Awake()
    {
        fogMesh = new Mesh { name = "Areas hidden by walls" };
        fogMesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = fogMesh;
    }

    public bool BlocksHull(Vector3 position, Quaternion rotation)
    {
        int count = Physics.OverlapBoxNonAlloc(position + Vector3.up * hullCenterHeight,
            hullHalfExtents, overlaps, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (Array.IndexOf(walls, overlaps[i]) >= 0)
                return true;
        return false;
    }

    public float TravelFraction(Vector3 position, Quaternion rotation, Vector3 travel)
    {
        float distance = travel.magnitude;
        if (distance <= 0f)
            return 1f;

        int count = Physics.BoxCastNonAlloc(position + Vector3.up * hullCenterHeight,
            hullHalfExtents, travel / distance, hits, rotation, distance + WallClearance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float allowed = distance;
        for (int i = 0; i < count; i++)
        {
            if (Array.IndexOf(walls, hits[i].collider) >= 0)
                allowed = Mathf.Min(allowed, hits[i].distance - WallClearance);
        }
        return Mathf.Clamp01(allowed / distance);
    }

    public bool CanSee(Vector3 eye, Vector3 worldPoint)
    {
        Vector3 delta = worldPoint - eye;
        float distance = delta.magnitude;
        if (distance <= 0.001f)
            return true;

        var ray = new Ray(eye, delta / distance);
        return DistanceToWall(ray, distance) >= distance;
    }

    public void UpdateFog(Vector3 eye, Bounds ground, float groundHeight)
    {
        if (hasEye && (eye - lastEye).sqrMagnitude < 0.0001f)
            return;

        hasEye = true;
        lastEye = eye;
        angles.Clear();
        vertices.Clear();
        triangles.Clear();

        // Cast on both sides of each wall corner to preserve hard visibility edges.
        AddCorners(eye, ground, false);
        foreach (BoxCollider wall in walls)
            if (wall != null && wall.enabled)
                AddWallCorners(eye, wall);
        angles.Sort();

        var previous = GetInterval(eye, ground, angles[angles.Count - 1]);
        float fogY = groundHeight + FogGroundOffset;
        foreach (float angle in angles)
        {
            SightInterval next = GetInterval(eye, ground, angle);
            if (previous.IsBlocked || next.IsBlocked)
            {
                int index = vertices.Count;
                AddFogVertex(previous.Near, fogY);
                AddFogVertex(previous.Far, fogY);
                AddFogVertex(next.Far, fogY);
                AddFogVertex(next.Near, fogY);
                triangles.Add(index);
                triangles.Add(index + 1);
                triangles.Add(index + 2);
                triangles.Add(index);
                triangles.Add(index + 2);
                triangles.Add(index + 3);
            }
            previous = next;
        }

        fogMesh.Clear();
        fogMesh.SetVertices(vertices);
        fogMesh.SetTriangles(triangles, 0, true);
    }

    private void AddCorners(Vector3 eye, Bounds box, bool includeEdges)
    {
        AddAngle(eye, box.min.x, box.min.z, includeEdges);
        AddAngle(eye, box.min.x, box.max.z, includeEdges);
        AddAngle(eye, box.max.x, box.min.z, includeEdges);
        AddAngle(eye, box.max.x, box.max.z, includeEdges);
    }

    private void AddWallCorners(Vector3 eye, BoxCollider wall)
    {
        Vector3 half = wall.size * 0.5f;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = wall.transform.TransformPoint(wall.center + new Vector3(x * half.x, 0f, z * half.z));
                AddAngle(eye, corner.x, corner.z, true);
            }
        }
    }

    private void AddAngle(Vector3 eye, float x, float z, bool includeEdges)
    {
        float angle = Mathf.Atan2(z - eye.z, x - eye.x);
        angles.Add(angle);
        if (includeEdges)
        {
            angles.Add(angle - CornerOffset);
            angles.Add(angle + CornerOffset);
        }
    }

    private SightInterval GetInterval(Vector3 eye, Bounds ground, float angle)
    {
        var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        float xEdge = DistanceToEdge(eye.x, direction.x, ground.min.x, ground.max.x);
        float zEdge = DistanceToEdge(eye.z, direction.z, ground.min.z, ground.max.z);
        float far = Mathf.Min(xEdge, zEdge);
        float near = DistanceToWall(new Ray(eye, direction), far);
        return new SightInterval(eye + direction * near, eye + direction * far, far - near > 0.02f);
    }

    private float DistanceToWall(Ray ray, float limit)
    {
        foreach (BoxCollider wall in walls)
        {
            if (wall != null && wall.enabled && wall.Raycast(ray, out RaycastHit hit, limit))
                limit = Mathf.Min(limit, hit.distance);
        }
        return limit;
    }

    private static float DistanceToEdge(float origin, float direction, float min, float max)
    {
        if (Mathf.Abs(direction) < 0.00001f)
            return float.PositiveInfinity;
        return Mathf.Max(0f, ((direction > 0f ? max : min) - origin) / direction);
    }

    private void AddFogVertex(Vector3 world, float height)
    {
        world.y = height;
        vertices.Add(transform.InverseTransformPoint(world));
    }

    private void OnDestroy()
    {
        if (fogMesh != null)
            Destroy(fogMesh);
    }

    private readonly struct SightInterval
    {
        public readonly Vector3 Near;
        public readonly Vector3 Far;
        public readonly bool IsBlocked;

        public SightInterval(Vector3 near, Vector3 far, bool isBlocked)
        {
            Near = near;
            Far = far;
            IsBlocked = isBlocked;
        }
    }
}
