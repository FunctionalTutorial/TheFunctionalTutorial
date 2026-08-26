namespace Content.Server.NPC.Pathfinding;

[Flags]
public enum PathFlags : byte
{
    None = 0,

    /// <summary>
    /// Do we have any form of access.
    /// </summary>
    Access = 1 << 0,

    /// <summary>
    /// Can we pry airlocks if necessary.
    /// </summary>
    Prying = 1 << 1,

    /// <summary>
    /// Can stuff like walls be broken.
    /// </summary>
    Smashing = 1 << 2,

    /// <summary>
    /// Can we climb it like a table or railing.
    /// </summary>
    Climbing = 1 << 3,

    /// <summary>
    /// Can we open stuff that requires interaction (e.g. click-open doors).
    /// </summary>
    Interact = 1 << 4,

    /// <summary>
    /// Doors are never an obstacle, locked or not: the route is costed as if they were open floor
    /// and the access check is not consulted. Distinct from <see cref="Interact"/>, which only
    /// covers doors that would let anybody through anyway.
    /// </summary>
    /// <remarks>
    /// Only says the pathfinder may route through them. Something still has to open the door when
    /// the mover arrives, so an NPC with this wants the access to match, or it will walk to a door
    /// that says no and stand there.
    /// </remarks>
    Doors = 1 << 5,
}
