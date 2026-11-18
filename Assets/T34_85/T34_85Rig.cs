using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class T34_85Rig : MonoBehaviour
{
    [Header("Articulation")]
    [SerializeField] private Transform turretPivot;
    [SerializeField] private Transform gunPivot;
    [SerializeField] private Transform muzzle;
    [SerializeField] private float turretYaw;
    [SerializeField, Range(-89f, 89f)] private float gunElevation;
    [SerializeField, Range(-89f, 89f)] private float minimumGunElevation = -10f;
    [SerializeField, Range(-89f, 89f)] private float maximumGunElevation = 30f;

    [Header("Tracks")]
    [SerializeField] private Transform leftTrack;
    [SerializeField] private Transform rightTrack;
    [SerializeField] private Transform leftWheels;
    [SerializeField] private Transform rightWheels;
    [SerializeField, Tooltip("Metres per second. Negative speed reverses this track.")]
    private float leftTrackSpeed;
    [SerializeField, Tooltip("Metres per second. Negative speed reverses this track.")]
    private float rightTrackSpeed;

    public Vector3 TurretPosition => turretPivot.position;
    public Vector3 GunPivotPosition => gunPivot.position;
    public Transform Muzzle => muzzle;
    public float TurretYaw { get => turretYaw; set => turretYaw = value; }
    public float MinGunElevation => Mathf.Min(minimumGunElevation, maximumGunElevation);
    public float MaxGunElevation => Mathf.Max(minimumGunElevation, maximumGunElevation);
    public float GunElevation
    {
        get => Mathf.Clamp(gunElevation, MinGunElevation, MaxGunElevation);
        set => gunElevation = Mathf.Clamp(value, MinGunElevation, MaxGunElevation);
    }
    public float HullPitch { get; set; }
    public float LeftTrackSpeed { get => leftTrackSpeed; set => leftTrackSpeed = value; }
    public float RightTrackSpeed { get => rightTrackSpeed; set => rightTrackSpeed = value; }

    private Transform bodyParent;
    private Transform hull;
    private Vector3 hullRestPosition;
    private Quaternion hullRestRotation;
    private Vector3 suspensionCenter;
    private Vector3 pitchAxis;
    private Vector3 turretRestPosition;
    private Quaternion turretRestRotation;
    private Quaternion aimFrameInBody;
    private Vector3 gunOffsetFromTurret;
    private Quaternion gunRestRotation;
    private Vector3 turretAxis;
    private Vector3 gunAxis;
    private TrackSide left;
    private TrackSide right;

    private void Awake()
    {
        left = new TrackSide(leftTrack, leftWheels);
        right = new TrackSide(rightTrack, rightWheels);

        Vector3 worldUp = leftTrack.TransformDirection(left.Up);
        Vector3 worldRight = leftTrack.TransformDirection(left.Axle);
        bodyParent = turretPivot.parent;
        turretAxis = bodyParent.InverseTransformDirection(worldUp);
        pitchAxis = bodyParent.InverseTransformDirection(worldRight);
        aimFrameInBody = Quaternion.Inverse(bodyParent.rotation) * transform.rotation;
        gunOffsetFromTurret = transform.InverseTransformDirection(gunPivot.position - turretPivot.position);
        gunAxis = gunPivot.parent.InverseTransformDirection(worldRight);
        turretRestPosition = turretPivot.localPosition;
        turretRestRotation = turretPivot.localRotation;
        gunRestRotation = gunPivot.localRotation;

        // Pitch around the height of the running gear and leave both track assemblies level.
        suspensionCenter = bodyParent.InverseTransformPoint(transform.TransformPoint(Vector3.up * 0.65f));
        hull = bodyParent.Find("Hull");
        if (hull != null)
        {
            hullRestPosition = hull.localPosition;
            hullRestRotation = hull.localRotation;
        }
    }

    public void GetAimAngles(Vector3 worldPoint, out float yaw, out float elevation)
    {
        Quaternion pitch = Quaternion.AngleAxis(HullPitch, pitchAxis);
        Quaternion frame = bodyParent.rotation * pitch * aimFrameInBody;
        Vector3 turretCenter = bodyParent.TransformPoint(PitchedPosition(turretRestPosition, pitch));
        Vector3 target = Quaternion.Inverse(frame) * (worldPoint - turretCenter);
        float radius = new Vector2(target.x, target.z).magnitude;
        yaw = radius > 0.001f ? Mathf.Atan2(target.x, target.z) * Mathf.Rad2Deg : TurretYaw;

        // The gun hinge is forward of and above the turret's yaw axis.
        float forward = Mathf.Max(radius - gunOffsetFromTurret.z, 0.001f);
        float height = target.y - gunOffsetFromTurret.y;
        elevation = Mathf.Clamp(Mathf.Atan2(height, forward) * Mathf.Rad2Deg, MinGunElevation, MaxGunElevation);
    }

    private void LateUpdate()
    {
        Step(Time.deltaTime);
    }

    private void Step(float deltaTime)
    {
        Quaternion pitch = Quaternion.AngleAxis(HullPitch, pitchAxis);
        if (hull != null)
        {
            hull.SetLocalPositionAndRotation(
                PitchedPosition(hullRestPosition, pitch),
                pitch * hullRestRotation);
        }

        turretPivot.SetLocalPositionAndRotation(
            PitchedPosition(turretRestPosition, pitch),
            pitch * Quaternion.AngleAxis(turretYaw, turretAxis) * turretRestRotation);
        gunPivot.localRotation = Quaternion.AngleAxis(-GunElevation, gunAxis) * gunRestRotation;
        left.Advance(leftTrackSpeed * deltaTime);
        right.Advance(rightTrackSpeed * deltaTime);
    }

    private Vector3 PitchedPosition(Vector3 restPosition, Quaternion pitch)
        => suspensionCenter + pitch * (restPosition - suspensionCenter);

    private sealed class TrackSide
    {
        // The exported shoes follow two straight runs joined by semicircles.
        private const float Radius = 0.552f;
        private const float StraightLength = 4.78f;
        private const float TurnLength = Mathf.PI * Radius;
        private const float Perimeter = 2f * (StraightLength + TurnLength);

        private static readonly float[] WheelRadii =
            { 0.44f, 0.414f, 0.425f, 0.424f, 0.425f, 0.414f, 0.448f };

        private readonly Transform[] links;
        private readonly Transform[] wheels;
        private readonly Quaternion[] wheelRestRotations;
        private readonly Quaternion linkRestRotation;
        private readonly Vector3 rearAxleCenter;
        private readonly Vector3 forward;
        private readonly Vector3 wheelAxis;
        private double distance;

        public Vector3 Up { get; }
        public Vector3 Axle { get; }

        public TrackSide(Transform track, Transform wheelGroup)
        {
            links = GetOrderedChildren(track);
            wheels = GetOrderedChildren(wheelGroup);

            Vector3 rearTop = links[0].localPosition;
            Vector3 frontBottom = links[links.Length / 2].localPosition;
            forward = (links[1].localPosition - rearTop).normalized;
            Up = Vector3.ProjectOnPlane(rearTop - frontBottom, forward).normalized;
            Axle = Vector3.Cross(Up, forward).normalized;
            rearAxleCenter = rearTop - Up * Radius;

            linkRestRotation = links[0].localRotation;
            wheelAxis = wheelGroup.InverseTransformDirection(track.TransformDirection(Axle));
            wheelRestRotations = new Quaternion[wheels.Length];
            for (int i = 0; i < wheels.Length; i++)
                wheelRestRotations[i] = wheels[i].localRotation;
        }

        public void Advance(float travel)
        {
            if (travel == 0f)
                return;

            distance += travel;
            float spacing = Perimeter / links.Length;
            float phase = (float)(distance % Perimeter);
            for (int i = 0; i < links.Length; i++)
                PlaceLink(links[i], Mathf.Repeat(phase + i * spacing, Perimeter));

            for (int i = 0; i < wheels.Length; i++)
            {
                float angle = (float)(distance / WheelRadii[i] * Mathf.Rad2Deg % 360.0);
                wheels[i].localRotation = Quaternion.AngleAxis(angle, wheelAxis) * wheelRestRotations[i];
            }
        }

        private void PlaceLink(Transform link, float pathDistance)
        {
            float along;
            float height;
            float angle;

            if (pathDistance < StraightLength)
            {
                along = pathDistance;
                height = Radius;
                angle = 0f;
            }
            else if (pathDistance < StraightLength + TurnLength)
            {
                angle = (pathDistance - StraightLength) / Radius;
                along = StraightLength + Radius * Mathf.Sin(angle);
                height = Radius * Mathf.Cos(angle);
            }
            else if (pathDistance < 2f * StraightLength + TurnLength)
            {
                along = 2f * StraightLength + TurnLength - pathDistance;
                height = -Radius;
                angle = Mathf.PI;
            }
            else
            {
                angle = (pathDistance - 2f * StraightLength) / Radius;
                along = Radius * Mathf.Sin(angle);
                height = Radius * Mathf.Cos(angle);
            }

            link.SetLocalPositionAndRotation(
                rearAxleCenter + forward * along + Up * height,
                Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Axle) * linkRestRotation);
        }

        private static Transform[] GetOrderedChildren(Transform parent)
        {
            var children = new Transform[parent.childCount];
            for (int i = 0; i < children.Length; i++)
                children[i] = parent.GetChild(i);

            Array.Sort(children, (a, b) => string.CompareOrdinal(a.name, b.name));
            return children;
        }
    }
}
