using AR.Iec61850.Mms;

namespace AR.Iec61850.Control;

/// <summary>
/// Resolves *command* semantics from a declared CDC and corroborating live MMS
/// evidence. A primitive MMS type is an encoding, not permission to Operate.
/// This is vendor-independent and never sends an MMS request.
/// </summary>
public static class Iec61850ControlSemanticResolver
{
    public static Iec61850ControlSemanticResolution Resolve(
        Iec61850ControlObjectDescriptor descriptor,
        string? declaredCdc,
        string? declaredStatusReference = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var declared = (declaredCdc ?? string.Empty).Trim().ToUpperInvariant();
        var inferred = (descriptor.Cdc ?? string.Empty).Trim().ToUpperInvariant();
        var status = descriptor.StatusReference ?? string.Empty;
        var mmsType = (descriptor.CtlValSpecification.MmsType ?? string.Empty).Trim().ToLowerInvariant();
        var size = descriptor.CtlValSpecification.Size;
        var cdc = string.IsNullOrWhiteSpace(declared) ? inferred : declared;

        Iec61850ControlSemanticResolution Deny(string reason)
            => new(false, cdc, reason);

        if (!descriptor.IsOperationallyReady)
            return Deny("The live control descriptor or ctlModel is not operationally ready.");
        if (string.IsNullOrWhiteSpace(status))
            return Deny("No live status reference corroborates the control semantics.");

        // A stale persisted feedback reference must not silently override a
        // different live BSC status object when commands are involved.
        if (cdc == "BSC" && !string.IsNullOrWhiteSpace(declaredStatusReference) &&
            !string.Equals(declaredStatusReference.Trim(), status, StringComparison.OrdinalIgnoreCase))
            return Deny("Declared BSC feedback reference disagrees with the live control descriptor.");

        if (cdc != inferred &&
            !(inferred == "INC/ISC" && cdc is "INC" or "ISC") &&
            !(cdc == "DPC" && inferred == "SPC" && IsStandardPosition(descriptor.ObjectReference)))
            return Deny($"Declared CDC '{cdc}' conflicts with live CDC evidence '{inferred}'.");

        var statusIsStep = status.EndsWith(".valWTr.posVal", StringComparison.OrdinalIgnoreCase);
        var statusIsState = status.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase);
        var supported = cdc switch
        {
            "BSC" => statusIsStep &&
                     (mmsType is "integer" or "unsigned" ||
                      (mmsType == "bit-string" && size == 2)),
            "DPC" => statusIsState &&
                     ((mmsType == "bit-string" && size == 2) ||
                      (mmsType == "boolean" && IsStandardPosition(descriptor.ObjectReference))),
            "SPC" => statusIsState && mmsType == "boolean",
            "INC" or "ISC" => declared == cdc && statusIsState &&
                               mmsType is "integer" or "unsigned" or "bcd",
            "APC" => declared == "APC" &&
                     (mmsType == "floating-point" || IsAnalogStructure(descriptor.CtlValSpecification)),
            _ => false // Other CDCs require an independently audited typed codec.
        };

        if (!supported)
            return Deny($"CDC '{cdc}' cannot be safely bound to live ctlVal '{descriptor.CtlValSpecification.Signature}' and status '{status}'.");

        return new(true, cdc,
            $"Verified CDC={cdc}; live ctlModel={descriptor.ControlModel}; ctlVal={descriptor.CtlValSpecification.Signature}; status={status}.");
    }

    internal static bool IsStandardPosition(string reference)
    {
        var suffix = reference[(reference.LastIndexOf('/') + 1)..];
        var dot = suffix.IndexOf('.');
        if (dot <= 0 || !suffix[(dot + 1)..].Equals("Pos", StringComparison.OrdinalIgnoreCase))
            return false;
        var ln = suffix[..dot].ToUpperInvariant();
        return ln.StartsWith("CSWI", StringComparison.Ordinal) ||
               ln.StartsWith("XCBR", StringComparison.Ordinal) ||
               ln.StartsWith("XSWI", StringComparison.Ordinal);
    }

    private static bool IsAnalogStructure(MmsTypeSpecificationNode ctlVal)
    {
        if (!ctlVal.MmsType.Equals("structure", StringComparison.OrdinalIgnoreCase))
            return false;
        var numeric = ctlVal.Children.Where(child =>
            child.MmsType is "integer" or "unsigned" or "floating-point").ToArray();
        return numeric.Length == 1 && ctlVal.Children.Count == 1;
    }
}

public sealed record Iec61850ControlSemanticResolution(bool IsCommandSafe, string Cdc, string Reason);
