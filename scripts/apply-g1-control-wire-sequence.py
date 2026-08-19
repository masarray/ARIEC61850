from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"expected block not found in {path}:\n{old[:600]}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")

models = ROOT / "src/AR.Iec61850/Control/Iec61850ControlModels.cs"
replace_once(
    models,
    '''public sealed class Iec61850ControlActionResult\n{\n''',
    '''public sealed class Iec61850ControlWireStep\n{\n    public Iec61850ControlAction Action { get; init; }\n    public string Reference { get; init; } = string.Empty;\n    public bool RequestAccepted { get; init; }\n    public string RequestHex { get; init; } = string.Empty;\n    public string ResponseHex { get; init; } = string.Empty;\n    public string Detail { get; init; } = string.Empty;\n}\n\npublic sealed class Iec61850ControlActionResult\n{\n''')
replace_once(
    models,
    '''    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();\n\n    public bool IsSuccess => RequestAccepted &&\n''',
    '''    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();\n    /// <summary>\n    /// Ordered wire-service evidence for the complete control transaction. For an\n    /// auto-selected SBO sequence this contains the Select/SBOw step before Operate,\n    /// rather than exposing only the last request on the association.\n    /// </summary>\n    public IReadOnlyList<Iec61850ControlWireStep> WireSteps { get; init; } = Array.Empty<Iec61850ControlWireStep>();\n\n    public bool IsSuccess => RequestAccepted &&\n''')

session = ROOT / "src/AR.Iec61850/Control/Iec61850ControlObjectSession.cs"
replace_once(
    session,
    '''        var stopwatch = Stopwatch.StartNew();\n        if (Descriptor.RequiresSelect && _activeSequence == null)\n''',
    '''        var stopwatch = Stopwatch.StartNew();\n        IReadOnlyList<Iec61850ControlWireStep> precedingWireSteps = Array.Empty<Iec61850ControlWireStep>();\n        if (Descriptor.RequiresSelect && _activeSequence == null)\n''')
replace_once(
    session,
    '''            if (!select.IsSuccess)\n                return select;\n        }\n        else if (!Descriptor.RequiresSelect)\n''',
    '''            if (!select.IsSuccess)\n                return select;\n            precedingWireSteps = select.WireSteps;\n        }\n        else if (!Descriptor.RequiresSelect)\n''')
replace_once(
    session,
    '''                return FromWriteFailure(Iec61850ControlAction.Operate, write, applicationError, context, stopwatch.Elapsed);\n            }\n\n            operateRequestAccepted = true;\n            if (!Descriptor.IsEnhanced)\n                return Accepted(Iec61850ControlAction.Operate, context, stopwatch.Elapsed, "Operate service accepted (normal-security completion boundary).");\n''',
    '''                return FromWriteFailure(\n                    Iec61850ControlAction.Operate,\n                    write,\n                    applicationError,\n                    context,\n                    stopwatch.Elapsed,\n                    precedingWireSteps);\n            }\n\n            operateRequestAccepted = true;\n            if (!Descriptor.IsEnhanced)\n                return Accepted(\n                    Iec61850ControlAction.Operate,\n                    context,\n                    stopwatch.Elapsed,\n                    "Operate service accepted (normal-security completion boundary).",\n                    precedingWireSteps);\n''')
replace_once(
    session,
    '''                    SequenceTimestamp = context.TimestampUtc,\n                    Elapsed = stopwatch.Elapsed\n                };\n            }\n\n            return new Iec61850ControlActionResult\n            {\n''',
    '''                    SequenceTimestamp = context.TimestampUtc,\n                    Elapsed = stopwatch.Elapsed,\n                    WireSteps = AppendWireSteps(\n                        precedingWireSteps,\n                        BuildWireStep(Iec61850ControlAction.Operate, requestAccepted: true, "Operate accepted; CommandTermination timed out."))\n                };\n            }\n\n            return new Iec61850ControlActionResult\n            {\n''')
replace_once(
    session,
    '''                SequenceTimestamp = context.TimestampUtc,\n                Elapsed = stopwatch.Elapsed\n            };\n        }\n        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)\n''',
    '''                SequenceTimestamp = context.TimestampUtc,\n                Elapsed = stopwatch.Elapsed,\n                WireSteps = AppendWireSteps(\n                    precedingWireSteps,\n                    BuildWireStep(\n                        Iec61850ControlAction.Operate,\n                        requestAccepted: true,\n                        termination.Positive ? "Operate accepted; positive CommandTermination received." : "Operate accepted; negative CommandTermination received."))\n            };\n        }\n        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)\n''')

# Make the helper methods retain the current service step and optionally prepend an auto-select step.
replace_once(
    session,
    '''    private Iec61850ControlActionResult Accepted(\n        Iec61850ControlAction action,\n        Iec61850ControlSequenceContext context,\n        TimeSpan elapsed,\n        string diagnostic)\n        => new()\n        {\n''',
    '''    private Iec61850ControlActionResult Accepted(\n        Iec61850ControlAction action,\n        Iec61850ControlSequenceContext context,\n        TimeSpan elapsed,\n        string diagnostic,\n        IReadOnlyList<Iec61850ControlWireStep>? precedingWireSteps = null)\n        => new()\n        {\n''')
replace_once(
    session,
    '''            Elapsed = elapsed,\n            Diagnostics = new[] { diagnostic }\n        };\n\n    private Iec61850ControlActionResult FromWriteFailure(\n''',
    '''            Elapsed = elapsed,\n            Diagnostics = new[] { diagnostic },\n            WireSteps = AppendWireSteps(\n                precedingWireSteps,\n                BuildWireStep(action, requestAccepted: true, diagnostic))\n        };\n\n    private Iec61850ControlActionResult FromWriteFailure(\n''')
replace_once(
    session,
    '''        Iec61850CommandTermination? appError,\n        Iec61850ControlSequenceContext context,\n        TimeSpan elapsed)\n        => new()\n        {\n''',
    '''        Iec61850CommandTermination? appError,\n        Iec61850ControlSequenceContext context,\n        TimeSpan elapsed,\n        IReadOnlyList<Iec61850ControlWireStep>? precedingWireSteps = null)\n        => new()\n        {\n''')
replace_once(
    session,
    '''            ControlNumber = context.ControlNumber,\n            SequenceTimestamp = context.TimestampUtc,\n            Elapsed = elapsed\n        };\n\n    private Iec61850ControlActionResult FromApplicationRejection(\n''',
    '''            ControlNumber = context.ControlNumber,\n            SequenceTimestamp = context.TimestampUtc,\n            Elapsed = elapsed,\n            WireSteps = AppendWireSteps(\n                precedingWireSteps,\n                BuildWireStep(action, requestAccepted: false, write.Message))\n        };\n\n    private Iec61850ControlActionResult FromApplicationRejection(\n''')
replace_once(
    session,
    '''            ControlNumber = context.ControlNumber,\n            SequenceTimestamp = context.TimestampUtc,\n            Elapsed = elapsed\n        };\n\n    private static Iec61850ControlActionResult SelectionTimedOut(\n''',
    '''            ControlNumber = context.ControlNumber,\n            SequenceTimestamp = context.TimestampUtc,\n            Elapsed = elapsed,\n            WireSteps = AppendWireSteps(\n                null,\n                BuildWireStep(action, requestAccepted: true, clientMessage))\n        };\n\n    private Iec61850ControlWireStep BuildWireStep(\n        Iec61850ControlAction action,\n        bool requestAccepted,\n        string detail)\n    {\n        var reference = action switch\n        {\n            Iec61850ControlAction.Select => Descriptor.References.Sbo,\n            Iec61850ControlAction.SelectWithValue => Descriptor.References.SboWithValue,\n            Iec61850ControlAction.Operate => Descriptor.References.Oper,\n            Iec61850ControlAction.Cancel => Descriptor.References.Cancel,\n            _ => Descriptor.References.Oper\n        };\n\n        return new Iec61850ControlWireStep\n        {\n            Action = action,\n            Reference = $"{reference.Domain}/{reference.Item}",\n            RequestAccepted = requestAccepted,\n            RequestHex = _transport.LastRequestHex,\n            ResponseHex = _transport.LastResponseHex,\n            Detail = detail\n        };\n    }\n\n    private static IReadOnlyList<Iec61850ControlWireStep> AppendWireSteps(\n        IReadOnlyList<Iec61850ControlWireStep>? preceding,\n        Iec61850ControlWireStep current)\n    {\n        if (preceding == null || preceding.Count == 0)\n            return new[] { current };\n        return preceding.Concat(new[] { current }).ToArray();\n    }\n\n    private static Iec61850ControlActionResult SelectionTimedOut(\n''')

# Extend the existing enhanced-SBO regression so the final result proves both writes in order.
test = ROOT / "tests/AR.Iec61850.Tests/Control/SmartControlStackTests.cs"
replace_once(
    test,
    '''        Assert.Equal(2, transport.Writes.Count);\n        Assert.EndsWith("$SBOw", transport.Writes[0].Reference.Item, StringComparison.Ordinal);\n        Assert.EndsWith("$Oper", transport.Writes[1].Reference.Item, StringComparison.Ordinal);\n    }\n\n    [Fact]\n    public async Task SboEnhanced_RejectsAsynchronousLastApplErrorDuringSelectWithValue()\n''',
    '''        Assert.Equal(2, transport.Writes.Count);\n        Assert.EndsWith("$SBOw", transport.Writes[0].Reference.Item, StringComparison.Ordinal);\n        Assert.EndsWith("$Oper", transport.Writes[1].Reference.Item, StringComparison.Ordinal);\n        Assert.Equal(2, result.WireSteps.Count);\n        Assert.Equal(Iec61850ControlAction.SelectWithValue, result.WireSteps[0].Action);\n        Assert.EndsWith("$SBOw", result.WireSteps[0].Reference, StringComparison.Ordinal);\n        Assert.Equal(Iec61850ControlAction.Operate, result.WireSteps[1].Action);\n        Assert.EndsWith("$Oper", result.WireSteps[1].Reference, StringComparison.Ordinal);\n    }\n\n    [Fact]\n    public async Task SboEnhanced_RejectsAsynchronousLastApplErrorDuringSelectWithValue()\n''')

print("G1 ordered control wire sequence evidence applied")
