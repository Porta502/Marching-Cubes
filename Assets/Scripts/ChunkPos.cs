using System.Collections.Generic;

public struct ChunkPos
{
    public int x, z;
    public ChunkPos(int x, int z) { this.x = x; this.z = z; }

    public override bool Equals(object obj) => obj is ChunkPos other && x == other.x && z == other.z;
    public override int GetHashCode()
    {
        unchecked { return x * 486187739 + z; }
    }
}