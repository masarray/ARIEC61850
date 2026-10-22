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
        // A saved SCL/DataSet SPC can describe a process projection, not the
        // command payload. Only an EXACT same-object ST step status plus a
        // typed two-bit MMS ctlVal establishes live directional BSC authority.
        // A bit-string alone (or a similarly named object) proves nothing.
        var exactLiveBsc =
            inferred == "BSC" &&
            mmsType == "bit-string" && size == 2 &&
            status.Equals(descriptor.ObjectReference + ".valWTr.posVal",
                StringComparison.OrdinalIgnoreCase);
        var liveBooleanDpc = inferred == "DPC" &&
            mmsType == "boolean" && descriptor.LiveStatusIsDoublePoint &&
            status.Equals(descriptor.ObjectReference + ".stVal", StringComparison.OrdinalIgnoreCase);
        var supersededAdvisorySpc = declared == "SPC" && exactLiveBsc;
        var supersededAdvisoryDpcSpc = declared == "SPC" && liveBooleanDpc;
        var cdc = supersededAdvisorySpc
            ? "BSC"
            : supersededAdvisoryDpcSpc ? "DPC"
            : string.IsNullOrWhiteSpace(declared) ? inferred : declared;

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
            !(cdc == "DPC" && inferred == "SPC" &&
              descriptor.LiveStatusIsDoublePoint && mmsType == "boolean"))
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
                      (mmsType == "boolean" &&
                       descriptor.LiveStatusIsDoublePoint &&
                       status.Equals(descriptor.ObjectReference + ".stVal",
                           StringComparison.OrdinalIgnoreCase))),
            "SPC" => statusIsState && mmsType == "boolean",
            "INC" or "ISC" => declared == cdc && statusIsState &&
                               mmsType is "integer" or "unsigned" or "bcd",
            "APC" => declared == "APC" &&
                     (mmsType == "floating-point" || IsAnalogStructure(descriptor.CtlValSpecification)),
            _ => false // Other CDCs require an independently audited typed codec.
        };

        if (!supported)
            return Deny($"CDC '{cdc}' cannot be safely bound to live ctlVal '{descriptor.CtlValSpecification.Signature}' and status '{status}'.");

        var provenance = supersededAdvisorySpc
            ? " Saved/DataSet CDC=SPC was advisory and superseded by exact live BSC control evidence."
            : supersededAdvisoryDpcSpc
                ? " Saved/DataSet CDC=SPC was advisory and superseded by live two-bit DPC status evidence."
                : string.Empty;
        return new(true, cdc,
            $"Verified CDC={cdc}; live ctlModel={descriptor.ControlModel}; ctlVal={descriptor.CtlValSpecification.Signature}; status={status}.{provenance}");
    }

    internal static bool IsStandardPosition(string reference)
    {
        var suffix = reference[(reference.LastIndexOf('/') + 1)..];
        var dot = suffix.IndexOf('.');
        if (dot <= 0 || !suffix[(dot + 1)..].Equals("Pos", StringComparison.OrdinalIgnoreCase))
            return false;
        var ln = suffix[..dot];
        // IEC 61850 LN identity is <prefix><lnClass><inst>, not necessarily
        // <lnClass><inst>. Examples: Bkr0XCBR2, Disc0XSWI1, BkrCSWI4.
        // Class is the final four letters before the REQUIRED instance digits,
        // regardless of the vendor/operator-configured prefix. Never infer a
        // position control from a substring or a DO name alone.
        var instanceStart = ln.Length;
        while (instanceStart > 0 && char.IsAsciiDigit(ln[instanceStart - 1]))
            instanceStart--;
        if (instanceStart == ln.Length || instanceStart < 4)
            return false;
        var classEnd = ln.AsSpan(0, instanceStart);
        return classEnd.EndsWith("CSWI".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               classEnd.EndsWith("XCBR".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               classEnd.EndsWith("XSWI".AsSpan(), StringComparison.OrdinalIgnoreCase);
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
