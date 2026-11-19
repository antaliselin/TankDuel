using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class TankDemoTarget : MonoBehaviour
{
    [SerializeField] private Transform aimPoint;
    [SerializeField] private GameObject visuals;
    [SerializeField] private GameObject hitIndicator;
    [SerializeField, Min(0.01f)] private float hitDisplayTime = 0.8f;
    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField, Min(0.01f)] private float respawnDelay = 3f;

    private BoxCollider hitCollider;
    private float hitTimeRemaining;
    private float respawnTimeRemaining;

    public Vector3 AimPoint => aimPoint.position;
    public Vector3 HealthBarPosition => aimPoint.TransformPoint(Vector3.up * 1.12f);
    public int MaxHealth => maxHealth;
    public int CurrentHealth { get; private set; }
    public bool IsAlive => CurrentHealth > 0;
    public bool IsVisible { get; private set; } = true;

    private void Awake()
    {
        hitCollider = GetComponent<BoxCollider>();
        Restore();
    }

    public void SetObserved(bool observed)
    {
        if (IsVisible == observed)
            return;

        IsVisible = observed;
        visuals.SetActive(IsAlive && observed);
    }

    public bool TakeDamage(int damage)
    {
        if (!IsAlive || damage <= 0)
            return false;

        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        if (IsAlive)
        {
            hitTimeRemaining = hitDisplayTime;
            hitIndicator.SetActive(true);
        }
        else
        {
            hitIndicator.SetActive(false);
            hitCollider.enabled = false;
            visuals.SetActive(false);
            respawnTimeRemaining = respawnDelay;
        }
        return true;
    }

    private void Update()
    {
        Advance(Time.deltaTime);
    }

    private void Advance(float deltaTime)
    {
        if (!IsAlive)
        {
            respawnTimeRemaining -= deltaTime;
            if (respawnTimeRemaining <= 0f)
                Restore();
            return;
        }

        if (hitTimeRemaining <= 0f)
            return;

        hitTimeRemaining -= deltaTime;
        if (hitTimeRemaining <= 0f)
            hitIndicator.SetActive(false);
    }

    private void Restore()
    {
        CurrentHealth = maxHealth;
        hitTimeRemaining = 0f;
        hitIndicator.SetActive(false);
        visuals.SetActive(IsVisible);
        hitCollider.enabled = true;
    }
}
