using System.Security.Cryptography;

namespace MasterSTI.Api.Common.Rendering;

// RFC 6962 §2.1 personalisation:
//   leaf hash    = SHA-256(0x00 || payload)
//   internal     = SHA-256(0x01 || left || right)
// Odd-leaf level duplicates the last leaf (NOT the RFC 6962 unbalanced-tree
// rule -- that one promotes the orphan; we duplicate to match the simpler
// scheme ADR-0008 froze for downstream verifier parity).
internal static class MerkleRoot
{
    public static byte[] LeafHash(ReadOnlySpan<byte> payload)
    {
        var buf = new byte[1 + payload.Length];
        buf[0] = 0x00;
        payload.CopyTo(buf.AsSpan(1));
        return SHA256.HashData(buf);
    }

    public static byte[] NodeHash(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        Span<byte> buf = stackalloc byte[1 + 32 + 32];
        buf[0] = 0x01;
        left.CopyTo(buf[1..]);
        right.CopyTo(buf[33..]);
        return SHA256.HashData(buf);
    }

    public static byte[] Compute(IReadOnlyList<byte[]> leaves)
    {
        if (leaves.Count == 0)
            throw new ArgumentException("Cannot compute Merkle root over zero leaves.", nameof(leaves));

        var level = leaves.ToList();
        while (level.Count > 1)
        {
            var next = new List<byte[]>((level.Count + 1) / 2);
            for (var i = 0; i < level.Count; i += 2)
            {
                var left = level[i];
                var right = i + 1 < level.Count ? level[i + 1] : level[i];
                next.Add(NodeHash(left, right));
            }
            level = next;
        }
        return level[0];
    }
}
