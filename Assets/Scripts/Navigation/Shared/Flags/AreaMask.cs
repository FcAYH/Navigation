namespace Navigation
{
    public enum AreaMask : uint
    {
        NotWalkable = 1u,
        Walkable = 2u,
        Grass = 4u,
        Desert = 8u,
        Swamp = 16u,
        Forest = 32u,
    }
}
