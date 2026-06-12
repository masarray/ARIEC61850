namespace AR.Iec61850.Ethernet;

public readonly record struct VlanTag(byte PriorityCodePoint, bool DropEligible, ushort VlanId)
{
    public VlanTag(byte priorityCodePoint, ushort vlanId)
        : this(priorityCodePoint, false, vlanId)
    {
    }

    public ushort ToTagControlInformation()
    {
        if (PriorityCodePoint > 7)
            throw new ArgumentOutOfRangeException(nameof(PriorityCodePoint), "VLAN priority must be 0..7.");

        if (VlanId > 4094)
            throw new ArgumentOutOfRangeException(nameof(VlanId), "VLAN ID must be 0..4094.");

        return (ushort)((PriorityCodePoint << 13) | (DropEligible ? 0x1000 : 0) | VlanId);
    }

    public static VlanTag FromTagControlInformation(ushort tci)
        => new((byte)((tci >> 13) & 0x07), (tci & 0x1000) != 0, (ushort)(tci & 0x0FFF));
}
