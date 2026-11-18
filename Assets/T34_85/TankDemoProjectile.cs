using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TankDemoProjectile : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float radius = 0.09f;
    [SerializeField] private ParticleSystem targetImpactEffect;

    [Header("Default round settings")]
    [SerializeField, Min(0.01f)] private float speed = 280f;
    [SerializeField, Min(0f)] private float gravityScale = 1f;
    [SerializeField, Min(0.01f)] private float lifetime = 2.5f;
    [SerializeField, Min(1)] private int damage = 25;

    private Action onTargetHit;
    private FlightState flight;
    private int flightDamage;

    public void Launch(Action onHit, TankDemoAmmoConfig ammo = null)
    {
        onTargetHit = onHit;
        flight = CreateFlight(transform.position, transform.forward, ammo);
        flightDamage = ammo != null ? ammo.Damage : damage;
    }

    public bool PredictImpact(Vector3 origin, Vector3 direction, out Vector3 point, TankDemoAmmoConfig ammo = null)
    {
        FlightState preview = CreateFlight(origin, direction, ammo);
        float step = Time.fixedDeltaTime;
        while (preview.RemainingLife > 0f)
        {
            if (TraceStep(ref preview, step, out RaycastHit hit))
            {
                point = hit.point;
                return true;
            }
        }

        point = preview.Position;
        return false;
    }

    private void FixedUpdate()
    {
        if (!Advance(Time.fixedDeltaTime))
            Destroy(gameObject);
    }

    private bool Advance(float deltaTime)
    {
        if (TraceStep(ref flight, deltaTime, out RaycastHit hit))
        {
            if (hit.collider.TryGetComponent(out TankDemoTarget target) && target.TakeDamage(flightDamage))
            {
                Vector3 explosionPoint = target.IsAlive ? hit.point : target.AimPoint;
                ParticleSystem explosion = Instantiate(targetImpactEffect, explosionPoint, Quaternion.LookRotation(hit.normal));
                if (!target.IsAlive)
                    explosion.transform.localScale *= 1.65f;
                onTargetHit?.Invoke();
            }

            return false;
        }

        transform.position = flight.Position;
        if (flight.Velocity.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(flight.Velocity);
        return flight.RemainingLife > 0f;
    }

    private FlightState CreateFlight(Vector3 origin, Vector3 direction, TankDemoAmmoConfig ammo)
    {
        return new FlightState
        {
            Position = origin,
            Velocity = direction.normalized * (ammo != null ? ammo.MuzzleSpeed : speed),
            Gravity = Physics.gravity * (ammo != null ? ammo.GravityScale : gravityScale),
            RemainingLife = ammo != null ? ammo.FlightLifetime : lifetime
        };
    }

    private bool TraceStep(ref FlightState state, float fixedStep, out RaycastHit hit)
    {
        float step = Mathf.Min(fixedStep, state.RemainingLife);
        Vector3 travel = (state.Velocity + state.Gravity * (0.5f * step)) * step;
        float distance = travel.magnitude;
        hit = default;
        if (distance > 0f && Physics.SphereCast(state.Position, radius, travel / distance, out hit, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return true;

        state.Position += travel;
        state.Velocity += state.Gravity * step;
        state.RemainingLife -= step;
        return false;
    }

    private struct FlightState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Gravity;
        public float RemainingLife;
    }
}
