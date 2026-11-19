using Cinemachine;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(T34_85Rig))]
[DefaultExecutionOrder(10)]
public sealed class T34_85DemoController : MonoBehaviour
{
    private const float ShotReticleInset = 20f;
    private static readonly Color ShotReticleColor = new Color(1f, 0.69f, 0.27f);
    private static readonly Color ReticleOutline = new Color(0.05f, 0.07f, 0.08f, 0.93f);

    [Header("Scene")]
    [SerializeField] private CinemachineBrain cameraBrain;
    [SerializeField] private Renderer drivingSurface;
    [SerializeField] private TankMudTerrain mudTerrain;

    [Header("Driving")]
    [SerializeField, Min(0f)] private float forwardSpeed = 6f;
    [SerializeField, Min(0f)] private float reverseSpeed = 3.4f;
    [SerializeField, Min(0f)] private float acceleration = 10f;
    [SerializeField, Min(0f)] private float turnSpeed = 84f;
    [SerializeField, Min(0f)] private float turnAcceleration = 300f;
    [SerializeField, Min(0f)] private float trackSpacing = 2.438f;
    [SerializeField, Min(0f)] private float edgeClearance = 6f;
    [SerializeField, Min(0f), Tooltip("Maximum forward or backward body tilt in degrees.")]
    private float maximumBodyPitch = 2.5f;
    [SerializeField, Min(0.01f), Tooltip("How quickly the suspension responds, in seconds.")]
    private float bodyPitchResponse = 0.16f;

    [Header("Mouse Aim")]
    [SerializeField, Min(0f), Tooltip("Maximum turret rotation in degrees per second.")]
    private float turretMaxSpeed = 90f;
    [SerializeField, Min(0f), Tooltip("Turret acceleration and braking in degrees per second squared.")]
    private float turretAcceleration = 240f;
    [SerializeField, Min(0f), Tooltip("Maximum gun elevation speed in degrees per second.")]
    private float gunElevationMaxSpeed = 32f;
    [SerializeField, Min(0f), Tooltip("Gun elevation acceleration and braking in degrees per second squared.")]
    private float gunElevationAcceleration = 100f;

    [Header("Firing")]
    [SerializeField] private TankDemoProjectile projectilePrefab;
    [SerializeField, Tooltip("The first ammunition type is selected at spawn.")]
    private TankDemoAmmoConfig[] ammunition = System.Array.Empty<TankDemoAmmoConfig>();
    [SerializeField] private ParticleSystem muzzleEffect;
    [SerializeField] private AudioSource fireAudioSource;

    private T34_85Rig rig;
    private Camera aimCamera;
    private TankDemoAmmoRack ammoRack;
    private float speed;
    private float yawSpeed;
    private float previousCenterSpeed;
    private float bodyPitchVelocity;
    private float turretYawSpeed;
    private float gunElevationSpeed;
    private float targetGunElevation;
    private Vector3 aimPoint;
    private bool showAimReticles;
    private Vector3 shotPoint;
    private bool shotWillHit;
    private bool fireRequested;
    private int hits;
    private TankDemoTarget[] practiceTargets;
    private GUIStyle elevationValueStyle;
    private GUIStyle healthLabelStyle;

    private void Awake()
    {
        rig = GetComponent<T34_85Rig>();
        aimCamera = cameraBrain.GetComponent<Camera>();
        ammoRack = new TankDemoAmmoRack(ammunition);
        aimPoint = rig.Muzzle.position + rig.Muzzle.forward * 30f;
        targetGunElevation = rig.GunElevation;
        practiceTargets = FindObjectsOfType<TankDemoTarget>();
    }

    private void Update()
    {
        int throttle = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
        int steer = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
        Vector2 cursor = Input.mousePosition;
        Step(throttle, steer, cursor, Time.deltaTime);
        if (Time.deltaTime > 0f)
            ammoRack.Cycle((Input.GetKeyDown(KeyCode.E) ? 1 : 0) - (Input.GetKeyDown(KeyCode.Q) ? 1 : 0));
        fireRequested = Time.deltaTime > 0f && Input.GetMouseButtonDown(0) && aimCamera.pixelRect.Contains(cursor);
    }

    private void LateUpdate()
    {
        // The rig has applied hull pitch, turret yaw, and gun elevation for this frame.
        if (showAimReticles && ammoRack.HasAmmo)
        {
            Transform muzzle = rig.Muzzle;
            shotWillHit = projectilePrefab.PredictImpact(muzzle.position, muzzle.forward, out shotPoint, ammoRack.Selected);
        }

        if (fireRequested)
            TryFire();
        fireRequested = false;
    }

    private void Step(float throttle, float steer, Vector2 cursor, float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        MoveTank(throttle, steer, deltaTime);
        cameraBrain.ManualUpdate();
        ammoRack.Tick(deltaTime);
        AimAtCursor(cursor, deltaTime);
    }

    private void MoveTank(float throttle, float steer, float deltaTime)
    {
        float targetSpeed = throttle * (throttle < 0f ? reverseSpeed : forwardSpeed);
        speed = Mathf.MoveTowards(speed, targetSpeed, acceleration * deltaTime);
        yawSpeed = Mathf.MoveTowards(yawSpeed, steer * turnSpeed, turnAcceleration * deltaTime);

        float yaw = yawSpeed * deltaTime;
        Vector3 heading = Quaternion.AngleAxis(yaw * 0.5f, Vector3.up) * transform.forward;
        Vector3 start = transform.position;
        Vector3 travel = heading * (speed * deltaTime);
        Bounds bounds = mudTerrain != null ? mudTerrain.GroundBounds : drivingSurface.bounds;
        float fraction = Mathf.Min(
            AvailableFraction(start.x, travel.x, bounds.min.x + edgeClearance, bounds.max.x - edgeClearance),
            AvailableFraction(start.z, travel.z, bounds.min.z + edgeClearance, bounds.max.z - edgeClearance));

        Quaternion rotation = Quaternion.AngleAxis(yaw, Vector3.up) * transform.rotation;
        transform.SetPositionAndRotation(start + travel * fraction, rotation);

        // In a right turn the left track covers the longer arc.
        float centerSpeed = speed * fraction;
        float turningSpeed = yawSpeed * Mathf.Deg2Rad * trackSpacing * 0.5f;
        rig.LeftTrackSpeed = centerSpeed + turningSpeed;
        rig.RightTrackSpeed = centerSpeed - turningSpeed;
        if (mudTerrain != null)
        {
            float ground = mudTerrain.UpdateTracks(transform, trackSpacing, rig.LeftTrackSpeed, rig.RightTrackSpeed, deltaTime);
            Vector3 position = transform.position;
            position.y = Mathf.MoveTowards(position.y, ground, deltaTime);
            transform.position = position;
        }

        float longitudinalAcceleration = (centerSpeed - previousCenterSpeed) / deltaTime;
        previousCenterSpeed = centerSpeed;
        float pitchRatio = Mathf.Clamp(-longitudinalAcceleration / Mathf.Max(acceleration, 0.01f), -1f, 1f);
        float targetPitch = maximumBodyPitch * pitchRatio;
        rig.HullPitch = Mathf.SmoothDamp(rig.HullPitch, targetPitch, ref bodyPitchVelocity,
            bodyPitchResponse, Mathf.Infinity, deltaTime);
    }

    private static float AvailableFraction(float position, float travel, float minimum, float maximum)
    {
        if (travel > 0f)
            return Mathf.Clamp01((maximum - position) / travel);
        if (travel < 0f)
            return Mathf.Clamp01((minimum - position) / travel);
        return 1f;
    }

    private bool TryFire()
    {
        TankDemoAmmoConfig round = ammoRack.Fire();
        if (round == null)
            return false;

        Transform muzzle = rig.Muzzle;
        TankDemoProjectile shot = Instantiate(projectilePrefab, muzzle.position, muzzle.rotation);
        shot.Launch(RegisterHit, round);
        Instantiate(muzzleEffect, muzzle.position, muzzle.rotation);
        fireAudioSource.PlayOneShot(fireAudioSource.clip);
        return true;
    }

    private void RegisterHit()
    {
        hits++;
    }

    private void AimAtCursor(Vector2 cursor, float deltaTime)
    {
        showAimReticles = aimCamera.pixelRect.Contains(cursor);
        if (showAimReticles)
        {
            Ray ray = aimCamera.ScreenPointToRay(cursor);
            if (Physics.Raycast(ray, out RaycastHit hit, aimCamera.farClipPlane,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                aimPoint = hit.point;
            }
            else
            {
                float height = mudTerrain != null ? mudTerrain.RestHeight : drivingSurface.bounds.center.y;
                var groundPlane = new Plane(Vector3.up, Vector3.up * height);
                showAimReticles = groundPlane.Raycast(ray, out float distance);
                if (showAimReticles)
                    aimPoint = ray.GetPoint(distance);
            }
        }

        rig.GetAimAngles(aimPoint, out float targetYaw, out targetGunElevation);
        float yawError = Mathf.DeltaAngle(rig.TurretYaw, targetYaw);
        float yawTravel = MoveAngularMotor(yawError, turretMaxSpeed, turretAcceleration, ref turretYawSpeed, deltaTime);
        rig.TurretYaw = Mathf.DeltaAngle(0f, rig.TurretYaw + yawTravel);

        float pitchError = targetGunElevation - rig.GunElevation;
        float pitchTravel = MoveAngularMotor(pitchError, gunElevationMaxSpeed, gunElevationAcceleration,
            ref gunElevationSpeed, deltaTime);
        rig.GunElevation += pitchTravel;
    }

    private static float MoveAngularMotor(float error, float maxSpeed, float acceleration, ref float velocity, float deltaTime)
    {
        float previousSpeed = velocity;
        float speedTowardTarget = previousSpeed * Mathf.Sign(error);
        float halfSpeedChange = acceleration * deltaTime * 0.5f;
        // Reserve angle for this frame's movement and the subsequent braking.
        float remainingAngle = Mathf.Abs(error) - speedTowardTarget * deltaTime * 0.5f;
        float brakingTerm = halfSpeedChange * halfSpeedChange + 2f * acceleration * remainingAngle;
        float brakingSpeed = Mathf.Max(0f, Mathf.Sqrt(Mathf.Max(0f, brakingTerm)) - halfSpeedChange);
        float speedLimit = Mathf.Min(maxSpeed, brakingSpeed);
        float desiredSpeed = Mathf.Clamp(error / Mathf.Max(0.12f, deltaTime), -speedLimit, speedLimit);
        velocity = Mathf.MoveTowards(previousSpeed, desiredSpeed, acceleration * deltaTime);
        return (previousSpeed + velocity) * 0.5f * deltaTime;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint)
            return;

        Color previous = GUI.color;
        DrawHealthBars();
        var panel = new Rect(18f, Screen.height - 62f, 413f, 44f);
        GUI.color = new Color(0.07f, 0.09f, 0.10f, 0.84f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(panel.x + 13f, panel.y + 5f, panel.width - 26f, 20f),
            "W/S  Drive     A/D  Steer     Mouse  Aim     LMB  Fire");
        GUI.Label(new Rect(panel.x + 13f, panel.y + 23f, 84f, 20f), $"Hits  {hits}");
        GUI.Label(new Rect(panel.x + 104f, panel.y + 23f, 138f, 20f), "+   Mouse point");
        GUI.color = ShotReticleColor;
        GUI.Label(new Rect(panel.x + 246f, panel.y + 23f, 154f, 20f), "◇   Shot impact");
        DrawAmmoHud(panel);
        DrawElevationHud();
        if (showAimReticles)
            DrawAimReticles();
        GUI.color = previous;
    }

    private void DrawAmmoHud(Rect controls)
    {
        var panel = new Rect(controls.x, controls.y - 72f, controls.width, 64f);
        Texture2D fill = Texture2D.whiteTexture;
        GUI.color = new Color(0.07f, 0.09f, 0.10f, 0.90f);
        GUI.DrawTexture(panel, fill);

        TankDemoAmmoConfig selected = ammoRack.Selected;
        GUI.color = Color.white;
        GUI.Label(new Rect(panel.x + 13f, panel.y + 5f, 254f, 22f),
            selected != null ? selected.name : "No ammunition configured");
        if (ammoRack.TypeCount > 1)
            GUI.Label(new Rect(panel.xMax - 136f, panel.y + 5f, 123f, 22f), "Q / E   Switch ammo");
        GUI.Label(new Rect(panel.x + 13f, panel.y + 27f, 254f, 20f),
            $"{(ammoRack.Loaded ? 1 : 0)} loaded    /    {ammoRack.Reserve} reserve");

        GUI.color = ammoRack.Loaded ? new Color(0.48f, 0.88f, 0.60f) : ShotReticleColor;
        string status = ammoRack.Loaded ? "READY" : ammoRack.HasAmmo
            ? $"RELOADING  {ammoRack.ReloadRemaining:0.0}s"
            : "OUT OF AMMO";
        GUI.Label(new Rect(panel.xMax - 136f, panel.y + 27f, 123f, 20f), status);

        var progress = new Rect(panel.x + 14f, panel.y + 51f, panel.width - 28f, 4f);
        Color statusColor = GUI.color;
        GUI.color = new Color(0.26f, 0.29f, 0.30f);
        GUI.DrawTexture(progress, fill);
        GUI.color = statusColor;
        progress.width *= ammoRack.ReloadProgress;
        GUI.DrawTexture(progress, fill);
    }

    private void DrawAimReticles()
    {
        Vector2 mouse = ProjectToGui(aimPoint);
        DrawReticleStroke(mouse + Vector2.left * 11f, mouse + Vector2.left * 4f, Color.white);
        DrawReticleStroke(mouse + Vector2.right * 4f, mouse + Vector2.right * 11f, Color.white);
        DrawReticleStroke(mouse + Vector2.up * 4f, mouse + Vector2.up * 11f, Color.white);
        DrawReticleStroke(mouse + Vector2.down * 11f, mouse + Vector2.down * 4f, Color.white);

        if (!ammoRack.HasAmmo)
            return;

        Rect view = aimCamera.pixelRect;
        if (view.width <= ShotReticleInset * 2f || view.height <= ShotReticleInset * 2f)
            return;

        var viewport = new Rect(view.xMin, Screen.height - view.yMax, view.width, view.height);
        Vector2 marker = FitShotMarker(ProjectToGui(shotPoint), viewport, out Vector2 edgeDirection);
        if (edgeDirection != Vector2.zero)
        {
            Vector2 across = new Vector2(-edgeDirection.y, edgeDirection.x);
            Vector2 tip = marker + edgeDirection * 8f;
            Vector2 tail = marker - edgeDirection * 6f;
            DrawReticleStroke(tail + across * 7f, tip, ShotReticleColor);
            DrawReticleStroke(tip, tail - across * 7f, ShotReticleColor);
        }
        else
        {
            Vector2 top = marker + Vector2.down * 16f;
            Vector2 bottom = marker + Vector2.up * 16f;
            Vector2 left = marker + Vector2.left * 16f;
            Vector2 right = marker + Vector2.right * 16f;
            DrawReticleStroke(top, right, ShotReticleColor);
            DrawReticleStroke(right, bottom, ShotReticleColor);
            DrawReticleStroke(bottom, left, ShotReticleColor);
            DrawReticleStroke(left, top, ShotReticleColor);
        }

        if (!shotWillHit)
        {
            Vector2 label = marker + (edgeDirection == Vector2.zero ? Vector2.up * 29f : -edgeDirection * 41f);
            var badge = new Rect(label.x - 28f, label.y - 9f, 56f, 18f);
            GUI.color = ReticleOutline;
            GUI.DrawTexture(badge, Texture2D.whiteTexture);
            GUI.color = ShotReticleColor;
            GUI.Label(new Rect(badge.x + 5f, badge.y - 1f, badge.width - 5f, badge.height + 2f), "NO HIT");
        }
    }

    private Vector2 ProjectToGui(Vector3 position)
    {
        Vector3 screen = aimCamera.WorldToScreenPoint(position);
        return new Vector2(screen.x, Screen.height - screen.y);
    }

    private static Vector2 FitShotMarker(Vector2 point, Rect viewport, out Vector2 edgeDirection)
    {
        if (viewport.Contains(point))
        {
            edgeDirection = Vector2.zero;
            return point;
        }

        Vector2 delta = point - viewport.center;
        Vector2 half = viewport.size * 0.5f - Vector2.one * ShotReticleInset;
        float scale = Mathf.Max(Mathf.Abs(delta.x) / half.x, Mathf.Abs(delta.y) / half.y);
        edgeDirection = delta.normalized;
        return viewport.center + delta / scale;
    }

    private static void DrawReticleStroke(Vector2 start, Vector2 end, Color color)
    {
        Vector2 line = end - start;
        float length = line.magnitude;
        Matrix4x4 previous = GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(line.y, line.x) * Mathf.Rad2Deg, start);
        GUI.color = ReticleOutline;
        GUI.DrawTexture(new Rect(start.x - 1f, start.y - 2f, length + 2f, 4f), Texture2D.whiteTexture);
        GUI.color = color;
        GUI.DrawTexture(new Rect(start.x, start.y - 1f, length, 2f), Texture2D.whiteTexture);
        GUI.matrix = previous;
    }

    private void DrawHealthBars()
    {
        healthLabelStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10,
            fontStyle = FontStyle.Bold
        };
        Texture2D fill = Texture2D.whiteTexture;
        foreach (TankDemoTarget target in practiceTargets)
        {
            if (target == null || !target.IsAlive)
                continue;

            Vector3 screen = aimCamera.WorldToScreenPoint(target.HealthBarPosition);
            if (screen.z <= 0f || !aimCamera.pixelRect.Contains(new Vector2(screen.x, screen.y)))
                continue;

            var bar = new Rect(screen.x - 36f, Screen.height - screen.y - 23f, 72f, 18f);
            float fraction = (float)target.CurrentHealth / target.MaxHealth;
            GUI.color = new Color(0.055f, 0.073f, 0.080f, 0.94f);
            GUI.DrawTexture(bar, fill);
            GUI.color = new Color(0.26f, 0.29f, 0.30f);
            GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 12f, 68f, 4f), fill);
            GUI.color = Color.Lerp(new Color(0.91f, 0.27f, 0.19f), new Color(0.36f, 0.85f, 0.47f), fraction);
            GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 12f, 68f * fraction, 4f), fill);
            GUI.color = Color.white;
            GUI.Label(new Rect(bar.x, bar.y - 1f, bar.width, 13f),
                $"{target.CurrentHealth} / {target.MaxHealth}", healthLabelStyle);
        }
    }

    private void DrawElevationHud()
    {
        var panel = new Rect(Screen.width - 252f, 18f, 234f, 111f);
        Texture2D fill = Texture2D.whiteTexture;
        GUI.color = new Color(0.07f, 0.09f, 0.10f, 0.90f);
        GUI.DrawTexture(panel, fill);
        GUI.color = Color.white;
        GUI.Label(new Rect(panel.x + 14f, panel.y + 7f, 206f, 20f), "GUN ELEVATION");
        GUI.Label(new Rect(panel.x + 14f, panel.y + 28f, 96f, 17f), "CURRENT");
        GUI.Label(new Rect(panel.x + 126f, panel.y + 28f, 96f, 17f), "TARGET");

        elevationValueStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(panel.x + 14f, panel.y + 44f, 96f, 29f),
            $"{rig.GunElevation:+0.0;-0.0;0.0}°", elevationValueStyle);
        var targetColor = new Color(1f, 0.76f, 0.36f);
        GUI.color = targetColor;
        GUI.Label(new Rect(panel.x + 126f, panel.y + 44f, 96f, 29f),
            $"{targetGunElevation:+0.0;-0.0;0.0}°", elevationValueStyle);

        var scale = new Rect(panel.x + 15f, panel.y + 82f, 204f, 3f);
        float actual = Mathf.InverseLerp(rig.MinGunElevation, rig.MaxGunElevation, rig.GunElevation);
        float target = Mathf.InverseLerp(rig.MinGunElevation, rig.MaxGunElevation, targetGunElevation);
        GUI.color = new Color(0.29f, 0.34f, 0.37f);
        GUI.DrawTexture(scale, fill);
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(scale.x + actual * scale.width - 2f, scale.y - 4f, 4f, 11f), fill);
        GUI.color = targetColor;
        GUI.DrawTexture(new Rect(scale.x + target * scale.width - 1f, scale.y - 4f, 2f, 11f), fill);
        GUI.color = new Color(0.75f, 0.80f, 0.83f);
        GUI.Label(new Rect(panel.x + 14f, panel.y + 91f, 96f, 17f), $"MIN  {rig.MinGunElevation:0.#}°");
        GUI.Label(new Rect(panel.x + 126f, panel.y + 91f, 96f, 17f), $"MAX  {rig.MaxGunElevation:0.#}°");
    }
}
