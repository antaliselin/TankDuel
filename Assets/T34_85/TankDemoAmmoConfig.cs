using UnityEngine;

[CreateAssetMenu(fileName = "Tank Ammunition", menuName = "Tank Demo/Ammunition")]
public sealed class TankDemoAmmoConfig : ScriptableObject
{
    [SerializeField, Min(0), Tooltip("Total rounds carried at spawn, including any chambered round.")]
    private int startingRounds = 24;
    [SerializeField, Min(0.01f)] private float reloadSeconds = 1.5f;
    [SerializeField, Min(1)] private int damage = 25;
    [SerializeField, Min(0.01f), Tooltip("Launch speed in Unity units per second.")]
    private float muzzleSpeed = 280f;
    [SerializeField, Min(0f)] private float gravityScale = 1f;
    [SerializeField, Min(0.01f)] private float flightLifetime = 2.5f;

    public int StartingRounds => startingRounds;
    public float ReloadSeconds => Mathf.Max(0.01f, reloadSeconds);
    public int Damage => damage;
    public float MuzzleSpeed => muzzleSpeed;
    public float GravityScale => gravityScale;
    public float FlightLifetime => flightLifetime;
}
