using System.Linq;
using UnityEngine;

internal sealed class TankDemoAmmoRack
{
    private readonly TankDemoAmmoConfig[] types;
    private readonly int[] reserves;
    private int selectedIndex;
    private float reloadDuration;

    public TankDemoAmmoConfig Selected => types.Length == 0 ? null : types[selectedIndex];
    public int TypeCount => types.Length;
    public int Reserve => reserves.Length == 0 ? 0 : reserves[selectedIndex];
    public bool Loaded { get; private set; }
    public bool HasAmmo => Loaded || Reserve > 0;
    public float ReloadRemaining { get; private set; }
    public float ReloadProgress => Loaded ? 1f : reloadDuration > 0f
        ? 1f - ReloadRemaining / reloadDuration
        : 0f;

    public TankDemoAmmoRack(TankDemoAmmoConfig[] loadout)
    {
        types = loadout.Where(config => config != null).Distinct().ToArray();
        reserves = types.Select(config => Mathf.Max(0, config.StartingRounds)).ToArray();
        if (Reserve > 0)
            ChamberRound();
    }

    public void Cycle(int direction)
    {
        if (types.Length < 2 || direction == 0)
            return;

        if (Loaded)
            reserves[selectedIndex]++;

        selectedIndex = (selectedIndex + (direction > 0 ? 1 : types.Length - 1)) % types.Length;
        Loaded = false;
        BeginReload();
    }

    public TankDemoAmmoConfig Fire()
    {
        if (!Loaded)
            return null;

        TankDemoAmmoConfig round = Selected;
        Loaded = false;
        BeginReload();
        return round;
    }

    public void Tick(float deltaTime)
    {
        if (ReloadRemaining <= 0f)
            return;

        ReloadRemaining = Mathf.Max(0f, ReloadRemaining - deltaTime);
        if (ReloadRemaining == 0f)
            ChamberRound();
    }

    private void BeginReload()
    {
        reloadDuration = Reserve > 0 ? Selected.ReloadSeconds : 0f;
        ReloadRemaining = reloadDuration;
    }

    private void ChamberRound()
    {
        reserves[selectedIndex]--;
        Loaded = true;
    }
}
