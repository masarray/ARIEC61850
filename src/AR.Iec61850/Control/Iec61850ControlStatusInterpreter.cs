using AR.Iec61850.Mms;
using System.Globalization;

namespace AR.Iec61850.Control;

public static class Iec61850ControlStatusInterpreter
{
    public static Iec61850ControlStatusResult Interpret(
        string reference,
        string cdc,
        MmsReadResult readResult)
    {
        ArgumentNullException.ThrowIfNull(readResult);
        var readAt = DateTimeOffset.UtcNow;
        if (!readResult.IsSuccess || readResult.Value == null)
        {
            return new Iec61850ControlStatusResult
            {
                IsSuccess = false,
                Reference = reference,
                State = Iec61850ControlStatusState.Unknown,
                DisplayValue = "Unknown",
                Message = string.IsNullOrWhiteSpace(readResult.Message) ? "Status read failed." : readResult.Message,
                ResponseHex = readResult.ResponseHexPreview,
                ReadAtUtc = readAt
            };
        }

        var value = readResult.Value;
        if (IsDpc(cdc) && TryDecodeBitString(value, out var doublePoint))
        {
            var (state, display) = doublePoint switch
            {
                0 => (Iec61850ControlStatusState.Intermediate, "Intermediate"),
                1 => (Iec61850ControlStatusState.Open, "OPEN"),
                2 => (Iec61850ControlStatusState.Closed, "CLOSED"),
                3 => (Iec61850ControlStatusState.Bad, "Bad / invalid"),
                _ => (Iec61850ControlStatusState.Unknown, $"Unknown ({doublePoint})")
            };
            return Success(reference, value, state, display, readResult, readAt);
        }

        if (value.Kind == MmsDataKind.Boolean)
        {
            var on = value.Value is true;
            return Success(
                reference,
                value,
                on ? Iec61850ControlStatusState.On : Iec61850ControlStatusState.Off,
                on ? "ON" : "OFF",
                readResult,
                readAt);
        }

        if (value.Kind is MmsDataKind.Integer or MmsDataKind.Unsigned or MmsDataKind.FloatingPoint)
        {
            var display = Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "Unknown";
            return Success(reference, value, Iec61850ControlStatusState.Numeric, display, readResult, readAt);
        }

        return Success(
            reference,
            value,
            Iec61850ControlStatusState.Unknown,
            MmsDataValueRenderer.ToCompactString(value, reference),
            readResult,
            readAt);
    }

    private static Iec61850ControlStatusResult Success(
        string reference,
        MmsDataValue rawValue,
        Iec61850ControlStatusState state,
        string display,
        MmsReadResult readResult,
        DateTimeOffset readAt)
        => new()
        {
            IsSuccess = true,
            Reference = reference,
            State = state,
            DisplayValue = display,
            Message = readResult.Message,
            RawValue = rawValue,
            ResponseHex = readResult.ResponseHexPreview,
            ReadAtUtc = readAt
        };

    private static bool IsDpc(string cdc)
        => cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase) ||
           cdc.Contains("double", StringComparison.OrdinalIgnoreCase);

    private static bool TryDecodeBitString(MmsDataValue value, out ulong numeric)
    {
        numeric = 0;
        if (value.Kind != MmsDataKind.BitString)
            return false;

        var encoded = value.RawValue.ToArray();
        if (encoded.Length < 2 || encoded[0] > 7)
            return false;

        var bitCount = checked((encoded.Length - 1) * 8 - encoded[0]);
        if (bitCount is <= 0 or > 64)
            return false;

        for (var encodedBit = 0; encodedBit < bitCount; encodedBit++)
        {
            var byteIndex = 1 + encodedBit / 8;
            var bitInByte = encodedBit % 8;
            if ((encoded[byteIndex] & (0x80 >> bitInByte)) == 0)
                continue;

            var numericBit = bitCount - 1 - encodedBit;
            numeric |= 1UL << numericBit;
        }

        return true;
    }
}
