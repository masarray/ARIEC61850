namespace AR.Iec61850.Asn1;

public readonly record struct BerTlv(
    byte EncodedTag,
    BerClass Class,
    bool Constructed,
    int TagNumber,
    ReadOnlyMemory<byte> Value);
