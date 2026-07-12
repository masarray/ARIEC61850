using AR.Iec61850.Mms;

namespace AR.Iec61850.Control;

internal static class Iec61850ControlValueBinder
{
    public static MmsDataValue Bind(Iec61850ControlValue value, MmsTypeSpecificationNode specification)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(specification);

        if (value.Kind == Iec61850ControlValueKind.RawMms)
        {
            var raw = (MmsDataValue)value.Value;
            Validate(raw, specification, specification.Name.Length == 0 ? "ctlVal" : specification.Name);
            return raw;
        }

        return BindCore(value, specification);
    }

    public static void Validate(MmsDataValue value, MmsTypeSpecificationNode specification, string path)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(specification);

        var expected = NormalizeType(specification.MmsType);
        var actual = value.Kind switch
        {
            MmsDataKind.Array => "array",
            MmsDataKind.Structure => "structure",
            MmsDataKind.Boolean => "boolean",
            MmsDataKind.BitString => "bit-string",
            MmsDataKind.Integer => "integer",
            MmsDataKind.Unsigned => "unsigned",
            MmsDataKind.FloatingPoint => "floating-point",
            MmsDataKind.OctetString => "octet-string",
            MmsDataKind.VisibleString => "visible-string",
            MmsDataKind.MmsString => "mms-string",
            MmsDataKind.BinaryTime => "binary-time",
            MmsDataKind.UtcTime => "utc-time",
            _ => "unknown"
        };

        if (!TypeCompatible(expected, actual))
            throw new InvalidOperationException($"MMS type mismatch at {path}: expected {specification.MmsType}, received {value.Kind}.");

        ValidateShape(value, specification, expected, path);

        if (expected is "structure" or "array")
        {
            if (specification.Children.Count > 0 && value.Children.Count != specification.Children.Count)
                throw new InvalidOperationException($"MMS structure size mismatch at {path}: expected {specification.Children.Count}, received {value.Children.Count}.");

            for (var i = 0; i < Math.Min(value.Children.Count, specification.Children.Count); i++)
            {
                var childName = string.IsNullOrWhiteSpace(specification.Children[i].Name) ? $"[{i}]" : specification.Children[i].Name;
                Validate(value.Children[i], specification.Children[i], $"{path}.{childName}");
            }
        }
    }


    private static void ValidateShape(
        MmsDataValue value,
        MmsTypeSpecificationNode specification,
        string expected,
        string path)
    {
        if (expected == "bit-string" && specification.Size is > 0)
        {
            var encoded = value.RawValue.ToArray();
            if (encoded.Length == 0)
                throw new InvalidOperationException($"Empty MMS bit-string at {path}.");

            var unusedBits = encoded[0];
            if (unusedBits > 7 || encoded.Length == 1)
                throw new InvalidOperationException($"Invalid MMS bit-string encoding at {path}.");

            var actualBits = checked((encoded.Length - 1) * 8 - unusedBits);
            if (actualBits != specification.Size.Value)
                throw new InvalidOperationException($"MMS bit-string size mismatch at {path}: expected {specification.Size.Value} bits, received {actualBits}.");
        }

        if (expected == "octet-string" && specification.Size is > 0 && value.RawValue.Count > specification.Size.Value)
            throw new InvalidOperationException($"MMS octet-string at {path} exceeds the live limit of {specification.Size.Value} octets.");

        if ((expected is "visible-string" or "mms-string") &&
            specification.Size is > 0 &&
            System.Text.Encoding.UTF8.GetByteCount(value.Value as string ?? string.Empty) > specification.Size.Value)
        {
            throw new InvalidOperationException($"MMS string at {path} exceeds the live limit of {specification.Size.Value} octets.");
        }
    }

    private static MmsDataValue BindCore(Iec61850ControlValue value, MmsTypeSpecificationNode specification)
    {
        var type = NormalizeType(specification.MmsType);
        return type switch
        {
            "boolean" => MmsDataValue.Boolean(ToBoolean(value)),
            "bit-string" => BindBitString(value, specification),
            "integer" or "bcd" => MmsDataValue.Integer(ToInteger(value)),
            "unsigned" => MmsDataValue.Unsigned(ToUnsigned(value)),
            "floating-point" => MmsDataValue.FloatingPoint(ToDouble(value)),
            "structure" => BindStructure(value, specification),
            _ => throw new NotSupportedException($"ctlVal MMS type '{specification.MmsType}' is not supported by the smart control binder.")
        };
    }

    private static MmsDataValue BindBitString(Iec61850ControlValue value, MmsTypeSpecificationNode specification)
    {
        var numeric = value.Kind switch
        {
            Iec61850ControlValueKind.DoublePoint => (int)(Iec61850DoublePointValue)value.Value,
            Iec61850ControlValueKind.Integer => checked((int)(long)value.Value),
            Iec61850ControlValueKind.Unsigned => checked((int)(ulong)value.Value),
            _ => throw new InvalidOperationException($"A {value.Kind} value cannot be bound to MMS bit-string ctlVal.")
        };

        var bitCount = specification.Size.GetValueOrDefault(2);
        if (bitCount is <= 0 or > 32)
            bitCount = 2;

        var unsignedNumeric = checked((ulong)numeric);
        var limit = 1UL << bitCount;
        if (unsignedNumeric >= limit)
            throw new ArgumentOutOfRangeException(nameof(value), $"Value {numeric} does not fit the {bitCount}-bit ctlVal.");

        var byteCount = (bitCount + 7) / 8;
        var bytes = new byte[byteCount];
        for (var encodedBit = 0; encodedBit < bitCount; encodedBit++)
        {
            // MMS BIT STRING bit 0 is the most-significant transmitted bit.
            // Convert the ordinary numeric representation (for example DPC
            // off=01, on=10) into that network bit order.
            var numericBit = bitCount - 1 - encodedBit;
            if ((unsignedNumeric & (1UL << numericBit)) != 0)
            {
                var byteIndex = encodedBit / 8;
                var bitInByte = encodedBit % 8;
                bytes[byteIndex] |= (byte)(0x80 >> bitInByte);
            }
        }

        var unusedBits = checked((byte)(byteCount * 8 - bitCount));
        return MmsDataValue.BitString(unusedBits, bytes);
    }

    private static MmsDataValue BindStructure(Iec61850ControlValue value, MmsTypeSpecificationNode specification)
    {
        if (specification.Children.Count == 0)
            throw new InvalidOperationException("ctlVal is a structure but the live MMS specification has no components.");

        if (value.Kind == Iec61850ControlValueKind.StepPosition)
        {
            var step = (Iec61850StepPosition)value.Value;
            var children = specification.Children.Select(child =>
            {
                var name = NormalizeName(child.Name);
                if (name is "posval" or "position" or "i")
                    return BindCore(Iec61850ControlValue.Integer(step.Position), child);
                if (name is "transind" or "transient")
                    return MmsDataValue.Boolean(step.Transient);

                throw new NotSupportedException($"Unsupported ValWithTrans component '{child.Name}'.");
            });
            return MmsDataValue.Structure(children);
        }

        // AnalogueValue variants are represented by the exact live structure.
        // Bind the scalar to the single numeric component or to a named i/f member.
        if (value.Kind is Iec61850ControlValueKind.FloatingPoint or Iec61850ControlValueKind.Integer or Iec61850ControlValueKind.Unsigned)
        {
            var numericChildren = specification.Children
                .Select((child, index) => (child, index))
                .Where(x => IsNumericType(x.child.MmsType))
                .ToArray();

            if (numericChildren.Length == 1)
            {
                var selectedIndex = numericChildren[0].index;
                var bound = new MmsDataValue[specification.Children.Count];
                for (var i = 0; i < specification.Children.Count; i++)
                {
                    if (i == selectedIndex)
                        bound[i] = BindCore(value, specification.Children[i]);
                    else
                        bound[i] = DefaultValue(specification.Children[i]);
                }
                return MmsDataValue.Structure(bound);
            }

            var named = specification.Children
                .Select((child, index) => (child, index, name: NormalizeName(child.Name)))
                .FirstOrDefault(x => value.Kind == Iec61850ControlValueKind.FloatingPoint ? x.name == "f" : x.name == "i");

            if (named.child != null)
            {
                var bound = specification.Children.Select((child, index) =>
                    index == named.index ? BindCore(value, child) : DefaultValue(child));
                return MmsDataValue.Structure(bound);
            }
        }

        if (specification.Children.Count == 1)
            return MmsDataValue.Structure(new[] { BindCore(value, specification.Children[0]) });

        throw new NotSupportedException($"Cannot map {value.Kind} to ctlVal structure '{specification.Signature}'. Use Iec61850ControlValue.Raw for this vendor-specific type.");
    }

    private static MmsDataValue DefaultValue(MmsTypeSpecificationNode specification)
        => NormalizeType(specification.MmsType) switch
        {
            "boolean" => MmsDataValue.Boolean(false),
            "bit-string" => ZeroBitString(specification.Size),
            "integer" or "bcd" => MmsDataValue.Integer(0),
            "unsigned" => MmsDataValue.Unsigned(0),
            "floating-point" => MmsDataValue.FloatingPoint(0f),
            "octet-string" => MmsDataValue.OctetString(ReadOnlySpan<byte>.Empty),
            "visible-string" => MmsDataValue.VisibleString(string.Empty),
            "mms-string" => MmsDataValue.MmsString(string.Empty),
            "structure" => MmsDataValue.Structure(specification.Children.Select(DefaultValue)),
            _ => throw new NotSupportedException($"No safe default exists for MMS type '{specification.MmsType}'.")
        };

    private static MmsDataValue ZeroBitString(int? requestedBitCount)
    {
        var bitCount = requestedBitCount.GetValueOrDefault(1);
        if (bitCount is <= 0 or > 1024)
            bitCount = 1;

        var byteCount = (bitCount + 7) / 8;
        var unusedBits = checked((byte)(byteCount * 8 - bitCount));
        return MmsDataValue.BitString(unusedBits, new byte[byteCount]);
    }

    private static bool ToBoolean(Iec61850ControlValue value)
        => value.Kind switch
        {
            Iec61850ControlValueKind.Boolean => (bool)value.Value,
            Iec61850ControlValueKind.Integer => (long)value.Value != 0,
            Iec61850ControlValueKind.Unsigned => (ulong)value.Value != 0,
            _ => throw new InvalidOperationException($"A {value.Kind} value cannot be bound to MMS boolean ctlVal.")
        };

    private static long ToInteger(Iec61850ControlValue value)
        => value.Kind switch
        {
            Iec61850ControlValueKind.Integer => (long)value.Value,
            Iec61850ControlValueKind.Unsigned => checked((long)(ulong)value.Value),
            Iec61850ControlValueKind.DoublePoint => (int)(Iec61850DoublePointValue)value.Value,
            Iec61850ControlValueKind.FloatingPoint => checked((long)(double)value.Value),
            _ => throw new InvalidOperationException($"A {value.Kind} value cannot be bound to MMS integer ctlVal.")
        };

    private static ulong ToUnsigned(Iec61850ControlValue value)
        => value.Kind switch
        {
            Iec61850ControlValueKind.Unsigned => (ulong)value.Value,
            Iec61850ControlValueKind.Integer => checked((ulong)(long)value.Value),
            Iec61850ControlValueKind.DoublePoint => checked((ulong)(int)(Iec61850DoublePointValue)value.Value),
            _ => throw new InvalidOperationException($"A {value.Kind} value cannot be bound to MMS unsigned ctlVal.")
        };

    private static double ToDouble(Iec61850ControlValue value)
        => value.Kind switch
        {
            Iec61850ControlValueKind.FloatingPoint => (double)value.Value,
            Iec61850ControlValueKind.Integer => (long)value.Value,
            Iec61850ControlValueKind.Unsigned => (ulong)value.Value,
            _ => throw new InvalidOperationException($"A {value.Kind} value cannot be bound to MMS floating-point ctlVal.")
        };

    private static bool IsNumericType(string mmsType)
        => NormalizeType(mmsType) is "integer" or "unsigned" or "floating-point" or "bcd";

    private static bool TypeCompatible(string expected, string actual)
        => expected == actual ||
           (expected == "bcd" && actual == "integer");

    private static string NormalizeType(string value) => value.Trim().ToLowerInvariant();
    private static string NormalizeName(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
