namespace ActionStacksEX;

/// <summary>
/// The role of the job, matching Lumina's ClassJob.Role IDs.
/// </summary>
public enum JobRole : byte
{
    /// <summary>
    /// No role.
    /// </summary>
    None = 0,

    /// <summary>
    /// Tank.
    /// </summary>
    Tank = 1,

    /// <summary>
    /// Melee DPS.
    /// </summary>
    Melee = 2,

    /// <summary>
    /// Ranged Physical DPS.
    /// </summary>
    RangedPhysical = 3,

    /// <summary>
    /// Ranged Magical DPS.
    /// </summary>
    RangedMagical = 4,

    /// <summary>
    /// Healer.
    /// </summary>
    Healer = 5,

    /// <summary>
    /// Any DPS role (Placeholder for custom logic).
    /// </summary>
    DPS = 100
}