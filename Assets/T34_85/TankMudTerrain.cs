using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Terrain), typeof(TerrainCollider))]
public sealed class TankMudTerrain : MonoBehaviour
{
    [SerializeField] private Rect mudArea = new Rect(29f, 42f, 22f, 17f);
    [SerializeField, Min(0.01f)] private float trackWidth = 0.54f;
    [SerializeField, Min(0.01f)] private float stampSpacing = 0.19f;
    [SerializeField, Min(0.01f)] private float maximumRutDepth = 0.16f;
    [SerializeField, Min(0.01f)] private float compressionPerStamp = 0.056f;
    [SerializeField, Min(0.01f)] private float terrainUpdateInterval = 0.08f;
    [SerializeField] private ParticleSystem mudSpray;

    private const int DryMudLayer = 1;
    private const int WetRutLayer = 2;
    private const float ContactHalfLength = 1.90f;
    private const int SamplesPerTrack = 9;

    private readonly ContactTrail[] trails = new ContactTrail[SamplesPerTrack * 2];
    private Terrain terrain;
    private TerrainCollider terrainCollider;
    private TerrainData original;
    private TerrainData runtime;
    private float[,] initialHeights;
    private float[,] heights;
    private float[,,] initialColors;
    private Vector3 origin;
    private Vector3 size;
    private Vector2 heightStep;
    private RectInt? dirty;
    private float updateClock;

    public Bounds GroundBounds => new Bounds(origin + size * 0.5f, size);
    public float RestHeight => origin.y + initialHeights[0, 0] * size.y;

    private void Awake()
    {
        terrain = GetComponent<Terrain>();
        terrainCollider = GetComponent<TerrainCollider>();
        original = terrain.terrainData;
        runtime = Instantiate(original);
        terrain.terrainData = runtime;
        terrainCollider.terrainData = runtime;

        origin = terrain.GetPosition();
        size = runtime.size;
        int resolution = runtime.heightmapResolution;
        heightStep = new Vector2(size.x, size.z) / (resolution - 1);
        initialHeights = runtime.GetHeights(0, 0, resolution, resolution);
        heights = (float[,])initialHeights.Clone();
        initialColors = runtime.GetAlphamaps(0, 0, runtime.alphamapWidth, runtime.alphamapHeight);
    }

    public float UpdateTracks(Transform vehicle, float spacing, float leftSpeed, float rightSpeed, float deltaTime)
    {
        Vector3 front = vehicle.forward;
        Vector3 halfAxle = vehicle.right * (spacing * 0.5f);
        float scrub = Mathf.InverseLerp(1f, 5f, Mathf.Abs(leftSpeed - rightSpeed));
        UpdateSide(0, vehicle.position - halfAxle, front, -vehicle.right, leftSpeed, scrub);
        UpdateSide(SamplesPerTrack, vehicle.position + halfAxle, front, vehicle.right, rightSpeed, scrub);

        updateClock += deltaTime;
        if (dirty.HasValue && updateClock >= terrainUpdateInterval)
        {
            ApplyTerrainChanges(dirty.Value);
            dirty = null;
            updateClock = 0f;
        }

        Vector3 frontOffset = front * ContactHalfLength;
        return (SampleHeight(vehicle.position + frontOffset - halfAxle) +
                SampleHeight(vehicle.position + frontOffset + halfAxle) +
                SampleHeight(vehicle.position - frontOffset - halfAxle) +
                SampleHeight(vehicle.position - frontOffset + halfAxle)) * 0.25f;
    }

    private void UpdateSide(int start, Vector3 center, Vector3 front, Vector3 outward, float speed, float scrub)
    {
        Vector2 forward = new Vector2(front.x, front.z).normalized;
        for (int i = 0; i < SamplesPerTrack; i++)
        {
            float lengthPosition = 2f * i / (SamplesPerTrack - 1) - 1f;
            Vector3 sample = center + front * (lengthPosition * ContactHalfLength);
            Vector2 point = new Vector2(sample.x - origin.x, sample.z - origin.z);
            ref ContactTrail trail = ref trails[start + i];
            bool extraSample = i % (SamplesPerTrack / 2) != 0;
            if ((extraSample && scrub < 0.30f) || Mathf.Abs(speed) < 0.04f || !mudArea.Contains(point))
            {
                trail.Active = false;
                continue;
            }

            if (!trail.Active || Vector2.Distance(trail.Last, point) > 3f)
            {
                trail.Active = true;
                trail.Last = point;
                Stamp(point, forward, scrub);
                continue;
            }

            Vector2 shift = point - trail.Last;
            float travel = shift.magnitude;
            Vector2 direction = shift.normalized;
            // Sample by travelled distance so slow and fast traversal leave the same tread spacing.
            for (float offset = stampSpacing; offset <= travel; offset += stampSpacing)
            {
                Vector2 stamp = trail.Last + direction * stampSpacing;
                trail.Last = stamp;
                Stamp(stamp, forward, scrub);
                if (i == SamplesPerTrack / 2 && mudSpray != null && Random.value < 0.35f + scrub * 0.5f)
                    EmitMud(stamp, front * Mathf.Sign(speed), outward);
            }
        }
    }

    private void Stamp(Vector2 point, Vector2 forward, float scrub)
    {
        const float shoulder = 0.11f;
        float halfWidth = trackWidth * 0.5f;
        float halfLength = Mathf.Lerp(0.16f, 0.34f, scrub);
        float radius = halfWidth + shoulder + halfLength;
        int limit = runtime.heightmapResolution - 1;
        int left = Mathf.Max(0, Mathf.FloorToInt((point.x - radius) / heightStep.x));
        int right = Mathf.Min(limit, Mathf.CeilToInt((point.x + radius) / heightStep.x));
        int bottom = Mathf.Max(0, Mathf.FloorToInt((point.y - radius) / heightStep.y));
        int top = Mathf.Min(limit, Mathf.CeilToInt((point.y + radius) / heightStep.y));
        Vector2 side = new Vector2(-forward.y, forward.x);

        for (int z = bottom; z <= top; z++)
        {
            for (int x = left; x <= right; x++)
            {
                Vector2 sample = new Vector2(x * heightStep.x, z * heightStep.y);
                if (!mudArea.Contains(sample))
                    continue;

                Vector2 offset = sample - point;
                float nose = Mathf.Abs(Vector2.Dot(offset, forward)) / halfLength;
                float across = Mathf.Abs(Vector2.Dot(offset, side));
                float lengthWeight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 1f, nose));
                float border = Mathf.Min(sample.x - mudArea.xMin, mudArea.xMax - sample.x);
                border = Mathf.Min(border, Mathf.Min(sample.y - mudArea.yMin, mudArea.yMax - sample.y));
                lengthWeight *= Mathf.SmoothStep(0f, 1f, border / 0.75f);
                if (lengthWeight <= 0f || across >= halfWidth + shoulder)
                    continue;

                float current = heights[z, x];
                float baseline = initialHeights[z, x];
                float next = current;
                if (across < halfWidth)
                {
                    float edge = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(halfWidth * 0.65f, halfWidth, across));
                    float floor = baseline - maximumRutDepth * Mathf.Lerp(0.50f, 1f, lengthWeight) * edge / size.y;
                    next = Mathf.Min(current, Mathf.Max(floor, current - compressionPerStamp * lengthWeight * edge / size.y));
                }
                else if (current >= baseline)
                {
                    float rim = Mathf.Sin((across - halfWidth) / shoulder * Mathf.PI);
                    next = Mathf.Min(baseline + 0.04f / size.y, current + 0.018f * rim * lengthWeight / size.y);
                }

                next = Mathf.Clamp01(next);
                if (Mathf.Abs(next - current) <= 0.00001f)
                    continue;

                heights[z, x] = next;
                IncludeSample(x, z);
            }
        }
    }

    private void IncludeSample(int x, int z)
    {
        if (!dirty.HasValue)
        {
            dirty = new RectInt(x, z, 1, 1);
            return;
        }

        RectInt region = dirty.Value;
        dirty = new RectInt(Mathf.Min(x, region.xMin), Mathf.Min(z, region.yMin), 0, 0)
        {
            xMax = Mathf.Max(x + 1, region.xMax),
            yMax = Mathf.Max(z + 1, region.yMax)
        };
    }

    private void ApplyTerrainChanges(RectInt region)
    {
        var patch = new float[region.height, region.width];
        for (int z = 0; z < region.height; z++)
            for (int x = 0; x < region.width; x++)
                patch[z, x] = heights[region.y + z, region.x + x];

        runtime.SetHeightsDelayLOD(region.x, region.y, patch);
        runtime.SyncHeightmap();

        int lastHeight = runtime.heightmapResolution - 1;
        int lastAlpha = runtime.alphamapWidth - 1;
        float toAlpha = (float)lastAlpha / lastHeight;
        int xMin = Mathf.Max(0, Mathf.FloorToInt(region.xMin * toAlpha) - 1);
        int zMin = Mathf.Max(0, Mathf.FloorToInt(region.yMin * toAlpha) - 1);
        int xMax = Mathf.Min(lastAlpha, Mathf.CeilToInt(region.xMax * toAlpha) + 1);
        int zMax = Mathf.Min(lastAlpha, Mathf.CeilToInt(region.yMax * toAlpha) + 1);
        var colors = new float[zMax - zMin + 1, xMax - xMin + 1, runtime.alphamapLayers];
        for (int z = zMin; z <= zMax; z++)
        {
            int hz = Mathf.RoundToInt(z / toAlpha);
            for (int x = xMin; x <= xMax; x++)
            {
                int hx = Mathf.RoundToInt(x / toAlpha);
                float depth = (initialHeights[hz, hx] - heights[hz, hx]) * size.y;
                float wet = initialColors[z, x, DryMudLayer] * Mathf.Clamp01(depth / maximumRutDepth) * 0.92f;
                for (int layer = 0; layer < runtime.alphamapLayers; layer++)
                    colors[z - zMin, x - xMin, layer] = initialColors[z, x, layer];
                colors[z - zMin, x - xMin, DryMudLayer] -= wet;
                colors[z - zMin, x - xMin, WetRutLayer] += wet;
            }
        }
        runtime.SetAlphamaps(xMin, zMin, colors);
    }

    private float SampleHeight(Vector3 point)
    {
        return origin.y + runtime.GetInterpolatedHeight(
            Mathf.Clamp01((point.x - origin.x) / size.x),
            Mathf.Clamp01((point.z - origin.z) / size.z));
    }

    private void EmitMud(Vector2 point, Vector3 travelDirection, Vector3 outward)
    {
        Vector3 position = origin + new Vector3(point.x, 0f, point.y) + outward * (trackWidth * 0.45f);
        position.y = SampleHeight(position) + 0.04f;
        var emission = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = outward * Random.Range(0.6f, 1.6f) - travelDirection * 0.55f +
                       Vector3.up * Random.Range(0.65f, 1.65f),
            startSize = Random.Range(0.07f, 0.17f),
            startLifetime = Random.Range(0.22f, 0.50f),
            startColor = new Color32(99, 64, 42, 202)
        };
        mudSpray.Emit(emission, 2);
    }

    private void OnDestroy()
    {
        if (runtime == null)
            return;
        if (terrain != null && terrain.terrainData == runtime)
            terrain.terrainData = original;
        if (terrainCollider != null && terrainCollider.terrainData == runtime)
            terrainCollider.terrainData = original;
        Destroy(runtime);
    }

    private struct ContactTrail
    {
        public bool Active;
        public Vector2 Last;
    }
}
