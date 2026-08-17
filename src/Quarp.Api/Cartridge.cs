namespace Quarp.Api;

/// <summary>
/// Base class of every Quarp cartridge.
/// <see cref="Init"/> runs once as "tick 0" and is part of the deterministic simulation;
/// <see cref="Update"/> runs exactly 60 times per game second; <see cref="Draw"/> must not
/// mutate game state (SPEC-8 §7). Wiring to the console core arrives in M1.
/// </summary>
public abstract class Cartridge
{
    public virtual void Init() { }
    public virtual void Update() { }
    public virtual void Draw() { }
}
