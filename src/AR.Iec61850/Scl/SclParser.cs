using System.Globalization;
using System.Xml.Linq;
using AR.Iec61850.Ethernet;

namespace AR.Iec61850.Scl;

public sealed class SclParser
{
    public SclDocument Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("SCL file path is empty.", nameof(filePath));

        using var stream = File.OpenRead(filePath);
        var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        return Parse(document, Path.GetFileName(filePath));
    }

    public SclDocument Parse(string xml, string sourceName = "")
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new ArgumentException("SCL XML is empty.", nameof(xml));

        return Parse(XDocument.Parse(xml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo), sourceName);
    }

    public SclDocument Parse(XDocument document, string sourceName = "")
    {
        var root = document.Root ?? throw new InvalidDataException("SCL document has no root element.");
        if (!Is(root, "SCL"))
            throw new InvalidDataException("The selected file is not an IEC 61850 SCL document.");

        var warnings = new List<string>();
        var ieds = ParseIeds(root).ToList();
        var typeIndex = SclTypeIndex.Build(root);
        var dataSets = ParseDataSets(root, typeIndex, warnings).ToList();
        var dataSetIndex = dataSets
            .GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var addressIndex = ParseCommunicationAddresses(root);
        var goose = ParseGooseStreams(root, dataSetIndex, addressIndex, warnings).ToList();
        var sv = ParseSampledValuesStreams(root, dataSetIndex, addressIndex, warnings).ToList();
        var reports = ParseReportControls(root, dataSetIndex, warnings).ToList();
        var conflicts = DetectConflicts(ieds, goose, sv).ToList();

        if (dataSets.Count == 0)
            warnings.Add("No DataSet element was found. Semantic mapping will be limited.");
        if (goose.Count == 0)
            warnings.Add("No GSEControl stream was found.");
        if (sv.Count == 0)
            warnings.Add("No SampledValueControl stream was found.");
        if (reports.Count == 0)
            warnings.Add("No ReportControl block was found.");

        var header = root.Elements().FirstOrDefault(e => Is(e, "Header"));

        return new SclDocument
        {
            SourceName = sourceName,
            NamespaceUri = root.Name.NamespaceName,
            HeaderId = Attr(header, "id"),
            HeaderVersion = Attr(header, "version"),
            HeaderRevision = Attr(header, "revision"),
            Edition = DetectEdition(root),
            Ieds = ieds,
            DataSets = dataSets,
            GooseStreams = goose,
            SampledValuesStreams = sv,
            ReportControls = reports,
            Warnings = warnings,
            Conflicts = conflicts
        };
    }

    private static IEnumerable<SclIed> ParseIeds(XElement root)
    {
        foreach (var ied in root.Elements().Where(e => Is(e, "IED")))
        {
            yield return new SclIed
            {
                Name = Attr(ied, "name"),
                Manufacturer = Attr(ied, "manufacturer"),
                Type = Attr(ied, "type"),
                ConfigVersion = Attr(ied, "configVersion")
            };
        }
    }

    private static IEnumerable<SclDataSet> ParseDataSets(XElement root, SclTypeIndex typeIndex, List<string> warnings)
    {
        foreach (var ied in root.Elements().Where(e => Is(e, "IED")))
        {
            var iedName = Attr(ied, "name");
            foreach (var lDevice in ied.Descendants().Where(e => Is(e, "LDevice")))
            {
                var ldInst = Attr(lDevice, "inst");
                foreach (var ln in lDevice.Elements().Where(e => Is(e, "LN0") || Is(e, "LN")))
                {
                    var lnPath = BuildLogicalNodePath(ln);
                    foreach (var dataSet in ln.Elements().Where(e => Is(e, "DataSet")))
                    {
                        var name = Attr(dataSet, "name");
                        var entries = new List<SclDataSetEntry>();
                        var index = 1;

                        foreach (var fcda in dataSet.Elements().Where(e => Is(e, "FCDA")))
                            entries.Add(BuildDataSetEntry(iedName, fcda, index++, typeIndex, warnings));

                        yield return new SclDataSet
                        {
                            Key = DataSetKey(iedName, ldInst, lnPath, name),
                            IedName = iedName,
                            LdInst = ldInst,
                            LogicalNodePath = lnPath,
                            Name = name,
                            Reference = BuildDataSetReference(iedName, ldInst, lnPath, name),
                            Entries = entries
                        };
                    }
                }
            }
        }
    }

    private static SclDataSetEntry BuildDataSetEntry(
        string fallbackIedName,
        XElement fcda,
        int index,
        SclTypeIndex typeIndex,
        ICollection<string> warnings)
    {
        var iedName = Attr(fcda, "iedName");
        if (string.IsNullOrWhiteSpace(iedName))
            iedName = fallbackIedName;

        var ldInst = Attr(fcda, "ldInst");
        var prefix = Attr(fcda, "prefix");
        var lnClass = Attr(fcda, "lnClass");
        var lnInst = Attr(fcda, "lnInst");
        var doName = Attr(fcda, "doName");
        var daName = Attr(fcda, "daName");
        var fc = Attr(fcda, "fc");
        var typeInfo = typeIndex.Resolve(iedName, ldInst, prefix, lnClass, lnInst, doName, daName, fc);
        var signalReference = BuildSignalReference(iedName, ldInst, prefix, lnClass, lnInst, doName, daName, fc);

        if (!typeInfo.Resolved && !string.IsNullOrWhiteSpace(signalReference))
            warnings.Add($"Type unresolved for {signalReference}.");

        return new SclDataSetEntry
        {
            Index = index,
            SignalReference = signalReference,
            IedName = iedName,
            LdInst = ldInst,
            Prefix = prefix,
            LnClass = lnClass,
            LnInst = lnInst,
            DoName = doName,
            DaName = daName,
            Fc = string.IsNullOrWhiteSpace(fc) ? typeInfo.Fc : fc,
            Cdc = typeInfo.Cdc,
            BType = typeInfo.BType,
            TypeId = typeInfo.TypeId,
            EnumType = typeInfo.EnumType,
            IsQuality = IsQualityAttribute(daName),
            IsTimestamp = IsTimestampAttribute(daName)
        };
    }

    private static Dictionary<string, SclStreamAddress> ParseCommunicationAddresses(XElement root)
    {
        var result = new Dictionary<string, SclStreamAddress>(StringComparer.OrdinalIgnoreCase);
        var communication = root.Elements().FirstOrDefault(e => Is(e, "Communication"));
        if (communication is null)
            return result;

        foreach (var connectedAp in communication.Descendants().Where(e => Is(e, "ConnectedAP")))
        {
            var iedName = Attr(connectedAp, "iedName");

            foreach (var gse in connectedAp.Elements().Where(e => Is(e, "GSE")))
                result[StreamAddressKey("GOOSE", iedName, Attr(gse, "ldInst"), Attr(gse, "cbName"))] = BuildAddress(gse);

            foreach (var smv in connectedAp.Elements().Where(e => Is(e, "SMV")))
                result[StreamAddressKey("SV", iedName, Attr(smv, "ldInst"), Attr(smv, "cbName"))] = BuildAddress(smv);
        }

        return result;
    }

    private static SclStreamAddress BuildAddress(XElement addressOwner)
    {
        var values = addressOwner.Descendants()
            .Where(e => Is(e, "P"))
            .GroupBy(e => Attr(e, "type"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (g.Last().Value ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);

        string Get(string type) => values.TryGetValue(type, out var value) ? value : string.Empty;

        var appIdText = Get("APPID");
        var macText = NormalizeMac(Get("MAC-Address"));
        _ = MacAddress.TryParse(macText, out var mac);

        return new SclStreamAddress
        {
            AppIdText = NormalizeAppIdText(appIdText),
            AppId = TryParseUInt16(appIdText, preferHexWithoutPrefix: true),
            DestinationMacText = macText,
            DestinationMac = string.IsNullOrWhiteSpace(macText) ? null : mac,
            VlanId = TryParseUInt16(Get("VLAN-ID"), preferHexWithoutPrefix: false),
            VlanPriority = TryParseByte(Get("VLAN-PRIORITY"), preferHexWithoutPrefix: false)
        };
    }

    private static IEnumerable<SclGooseStream> ParseGooseStreams(
        XElement root,
        IReadOnlyDictionary<string, SclDataSet> dataSets,
        IReadOnlyDictionary<string, SclStreamAddress> addressIndex,
        List<string> warnings)
    {
        foreach (var ied in root.Elements().Where(e => Is(e, "IED")))
        {
            var iedName = Attr(ied, "name");
            foreach (var lDevice in ied.Descendants().Where(e => Is(e, "LDevice")))
            {
                var ldInst = Attr(lDevice, "inst");
                foreach (var ln0 in lDevice.Elements().Where(e => Is(e, "LN0")))
                {
                    foreach (var control in ln0.Elements().Where(e => Is(e, "GSEControl")))
                    {
                        var name = Attr(control, "name");
                        var dataSetName = Attr(control, "datSet");
                        var dataSet = ResolveDataSet(dataSets, iedName, ldInst, "LLN0", dataSetName);

                        if (dataSet is null)
                            warnings.Add($"GOOSE {iedName}{ldInst}/LLN0$GO${name} references missing DataSet '{dataSetName}'.");

                        addressIndex.TryGetValue(StreamAddressKey("GOOSE", iedName, ldInst, name), out var address);

                        yield return new SclGooseStream
                        {
                            Kind = "GOOSE",
                            IedName = iedName,
                            LdInst = ldInst,
                            ControlName = name,
                            ControlBlockReference = $"{iedName}{ldInst}/LLN0$GO${name}",
                            GoId = Attr(control, "appID"),
                            DataSetName = dataSetName,
                            DataSetReference = dataSet?.Reference ?? BuildDataSetReference(iedName, ldInst, "LLN0", dataSetName),
                            ConfigurationRevision = UIntAttr(control, "confRev"),
                            Address = address ?? new SclStreamAddress(),
                            Entries = dataSet?.Entries ?? Array.Empty<SclDataSetEntry>(),
                            MinTimeMilliseconds = UIntAttr(control, "minTime"),
                            MaxTimeMilliseconds = UIntAttr(control, "maxTime")
                        };
                    }
                }
            }
        }
    }

    private static IEnumerable<SclSampledValuesStream> ParseSampledValuesStreams(
        XElement root,
        IReadOnlyDictionary<string, SclDataSet> dataSets,
        IReadOnlyDictionary<string, SclStreamAddress> addressIndex,
        List<string> warnings)
    {
        foreach (var ied in root.Elements().Where(e => Is(e, "IED")))
        {
            var iedName = Attr(ied, "name");
            foreach (var lDevice in ied.Descendants().Where(e => Is(e, "LDevice")))
            {
                var ldInst = Attr(lDevice, "inst");
                foreach (var ln0 in lDevice.Elements().Where(e => Is(e, "LN0")))
                {
                    foreach (var control in ln0.Elements().Where(e => Is(e, "SampledValueControl")))
                    {
                        var name = Attr(control, "name");
                        var dataSetName = Attr(control, "datSet");
                        var dataSet = ResolveDataSet(dataSets, iedName, ldInst, "LLN0", dataSetName);

                        if (dataSet is null)
                            warnings.Add($"SV {iedName}{ldInst}/LLN0$SV${name} references missing DataSet '{dataSetName}'.");

                        addressIndex.TryGetValue(StreamAddressKey("SV", iedName, ldInst, name), out var address);
                        var svId = FirstNonEmpty(Attr(control, "svID"), Attr(control, "smvID"));

                        yield return new SclSampledValuesStream
                        {
                            Kind = "SV",
                            IedName = iedName,
                            LdInst = ldInst,
                            ControlName = name,
                            ControlBlockReference = $"{iedName}{ldInst}/LLN0$SV${name}",
                            SvId = svId,
                            SmvId = Attr(control, "smvID"),
                            DataSetName = dataSetName,
                            DataSetReference = dataSet?.Reference ?? BuildDataSetReference(iedName, ldInst, "LLN0", dataSetName),
                            ConfigurationRevision = UIntAttr(control, "confRev"),
                            Address = address ?? new SclStreamAddress(),
                            Entries = dataSet?.Entries ?? Array.Empty<SclDataSetEntry>(),
                            SampleRate = UShortAttr(control, "smpRate"),
                            SampleMode = Attr(control, "smpMod"),
                            NoAsdu = Math.Max((ushort)1, UShortAttr(control, "nofASDU"))
                        };
                    }
                }
            }
        }
    }

    private static IEnumerable<SclReportControl> ParseReportControls(
        XElement root,
        IReadOnlyDictionary<string, SclDataSet> dataSets,
        List<string> warnings)
    {
        foreach (var ied in root.Elements().Where(e => Is(e, "IED")))
        {
            var iedName = Attr(ied, "name");
            foreach (var lDevice in ied.Descendants().Where(e => Is(e, "LDevice")))
            {
                var ldInst = Attr(lDevice, "inst");
                foreach (var ln in lDevice.Elements().Where(e => Is(e, "LN0") || Is(e, "LN")))
                {
                    var lnPath = BuildLogicalNodePath(ln);
                    foreach (var control in ln.Elements().Where(e => Is(e, "ReportControl")))
                    {
                        var name = Attr(control, "name");
                        var dataSetName = Attr(control, "datSet");
                        var dataSet = ResolveDataSet(dataSets, iedName, ldInst, lnPath, dataSetName);

                        if (dataSet is null)
                            warnings.Add($"Report {iedName}{ldInst}/{lnPath}${name} references missing DataSet '{dataSetName}'.");

                        var buffered = BoolAttr(control, "buffered");
                        yield return new SclReportControl
                        {
                            IedName = iedName,
                            LdInst = ldInst,
                            LogicalNodePath = lnPath,
                            Name = name,
                            ReportId = Attr(control, "rptID"),
                            DataSetName = dataSetName,
                            DataSetReference = dataSet?.Reference ?? BuildDataSetReference(iedName, ldInst, lnPath, dataSetName),
                            ControlBlockReference = $"{iedName}{ldInst}/{lnPath}${(buffered ? "BR" : "RP")}${name}",
                            Buffered = buffered,
                            Indexed = !string.Equals(Attr(control, "indexed"), "false", StringComparison.OrdinalIgnoreCase),
                            ConfigurationRevision = UIntAttr(control, "confRev"),
                            BufferTimeMilliseconds = UIntAttr(control, "bufTime"),
                            IntegrityPeriodMilliseconds = UIntAttr(control, "intgPd"),
                            Entries = dataSet?.Entries ?? Array.Empty<SclDataSetEntry>()
                        };
                    }
                }
            }
        }
    }

    private static IEnumerable<SclConflict> DetectConflicts(
        IEnumerable<SclIed> ieds,
        IEnumerable<SclGooseStream> goose,
        IEnumerable<SclSampledValuesStream> sv)
    {
        foreach (var group in ieds.Where(i => !string.IsNullOrWhiteSpace(i.Name)).GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                yield return new SclConflict
                {
                    Kind = "IED",
                    Key = group.Key,
                    Description = $"Duplicate IED name '{group.Key}' appears {group.Count()} times."
                };
            }
        }

        foreach (var conflict in DetectStreamAddressConflicts("GOOSE", goose))
            yield return conflict;

        foreach (var conflict in DetectStreamAddressConflicts("SV", sv))
            yield return conflict;
    }

    private static IEnumerable<SclConflict> DetectStreamAddressConflicts<T>(string kind, IEnumerable<T> streams)
        where T : SclProcessBusStream
    {
        foreach (var group in streams
                     .Where(s => s.Address.AppId.HasValue)
                     .GroupBy(s => $"{s.Address.AppId:X4}", StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() <= 1)
                continue;

            var names = string.Join(", ", group.Select(s => s.ControlBlockReference));
            yield return new SclConflict
            {
                Kind = kind,
                Key = $"APPID 0x{group.Key}",
                Description = $"Duplicate {kind} APPID 0x{group.Key}: {names}."
            };
        }
    }

    private static SclDataSet? ResolveDataSet(
        IReadOnlyDictionary<string, SclDataSet> dataSets,
        string iedName,
        string ldInst,
        string lnPath,
        string dataSetName)
    {
        if (string.IsNullOrWhiteSpace(dataSetName))
            return null;

        if (dataSets.TryGetValue(DataSetKey(iedName, ldInst, lnPath, dataSetName), out var direct))
            return direct;

        return dataSets.Values.FirstOrDefault(d =>
            string.Equals(d.IedName, iedName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(d.LdInst, ldInst, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(d.Name, dataSetName, StringComparison.OrdinalIgnoreCase));
    }

    private static SclEdition DetectEdition(XElement root)
    {
        var ns = root.Name.NamespaceName.ToLowerInvariant();

        if (ns.Contains("2003", StringComparison.Ordinal))
            return SclEdition.Edition1;

        if (ns.Contains("2007b4", StringComparison.Ordinal) ||
            ns.Contains("ed2.1", StringComparison.Ordinal) ||
            ns.Contains("edition2.1", StringComparison.Ordinal))
        {
            return SclEdition.Edition21;
        }

        if (ns.Contains("2007", StringComparison.Ordinal) || ns.Contains("scl", StringComparison.Ordinal))
            return SclEdition.Edition2;

        return SclEdition.Unknown;
    }

    private static string DataSetKey(string iedName, string ldInst, string lnPath, string dataSetName)
        => $"{iedName}|{ldInst}|{lnPath}|{dataSetName}";

    private static string StreamAddressKey(string kind, string iedName, string ldInst, string cbName)
        => $"{kind}|{iedName}|{ldInst}|{cbName}";

    private static string BuildDataSetReference(string iedName, string ldInst, string lnPath, string dataSetName)
        => string.IsNullOrWhiteSpace(dataSetName) ? string.Empty : $"{iedName}{ldInst}/{lnPath}${dataSetName}";

    private static string BuildLogicalNodePath(XElement ln)
        => Is(ln, "LN0") ? "LLN0" : $"{Attr(ln, "prefix")}{Attr(ln, "lnClass")}{Attr(ln, "inst")}";

    private static string BuildSignalReference(
        string iedName,
        string ldInst,
        string prefix,
        string lnClass,
        string lnInst,
        string doName,
        string daName,
        string fc)
    {
        var ln = $"{prefix}{lnClass}{lnInst}";
        var data = string.IsNullOrWhiteSpace(daName) ? doName : $"{doName}.{daName}";
        var fcText = string.IsNullOrWhiteSpace(fc) ? string.Empty : $" [{fc}]";
        return $"{iedName}/{ldInst}/{ln}.{data}{fcText}";
    }

    private static string NormalizeMac(string text)
        => string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim().Replace('-', ':').ToUpperInvariant();

    private static string NormalizeAppIdText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return $"0x{trimmed[2..].ToUpperInvariant()}";

        return $"0x{trimmed.ToUpperInvariant()}";
    }

    private static ushort? TryParseUInt16(string text, bool preferHexWithoutPrefix)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ushort.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;

        if (preferHexWithoutPrefix)
            return ushort.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;

        return ushort.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static byte? TryParseByte(string text, bool preferHexWithoutPrefix)
    {
        var parsed = TryParseUInt16(text, preferHexWithoutPrefix);
        return parsed <= byte.MaxValue ? (byte)parsed.Value : null;
    }

    private static uint UIntAttr(XElement element, string localName)
    {
        var text = Attr(element, localName);
        return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static ushort UShortAttr(XElement element, string localName)
    {
        var text = Attr(element, localName);
        return ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (ushort)0;
    }

    private static bool BoolAttr(XElement element, string localName)
        => string.Equals(Attr(element, localName), "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsQualityAttribute(string daName)
        => string.Equals(daName, "q", StringComparison.OrdinalIgnoreCase) ||
           daName.EndsWith(".q", StringComparison.OrdinalIgnoreCase);

    private static bool IsTimestampAttribute(string daName)
        => string.Equals(daName, "t", StringComparison.OrdinalIgnoreCase) ||
           daName.EndsWith(".t", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(string first, string second)
        => string.IsNullOrWhiteSpace(first) ? second : first;

    private static bool Is(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static string Attr(XElement? element, string localName)
    {
        if (element is null)
            return string.Empty;

        var attr = element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, localName, StringComparison.Ordinal));
        return attr?.Value?.Trim() ?? string.Empty;
    }

    private sealed class SclTypeIndex
    {
        private readonly Dictionary<string, XElement> _ieds;
        private readonly Dictionary<string, XElement> _lNodeTypes;
        private readonly Dictionary<string, XElement> _doTypes;
        private readonly Dictionary<string, XElement> _daTypes;
        private readonly HashSet<string> _enumTypeIds;

        private SclTypeIndex(
            Dictionary<string, XElement> ieds,
            Dictionary<string, XElement> lNodeTypes,
            Dictionary<string, XElement> doTypes,
            Dictionary<string, XElement> daTypes,
            HashSet<string> enumTypeIds)
        {
            _ieds = ieds;
            _lNodeTypes = lNodeTypes;
            _doTypes = doTypes;
            _daTypes = daTypes;
            _enumTypeIds = enumTypeIds;
        }

        public static SclTypeIndex Build(XElement root)
        {
            return new SclTypeIndex(
                BuildElementIndex(root.Elements().Where(e => Is(e, "IED")), "name"),
                BuildElementIndex(root.Descendants().Where(e => Is(e, "LNodeType")), "id"),
                BuildElementIndex(root.Descendants().Where(e => Is(e, "DOType")), "id"),
                BuildElementIndex(root.Descendants().Where(e => Is(e, "DAType")), "id"),
                root.Descendants()
                    .Where(e => Is(e, "EnumType") && !string.IsNullOrWhiteSpace(Attr(e, "id")))
                    .Select(e => Attr(e, "id"))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));
        }

        public TypeResolution Resolve(
            string iedName,
            string ldInst,
            string prefix,
            string lnClass,
            string lnInst,
            string doName,
            string daName,
            string fc)
        {
            var ln = FindLogicalNode(iedName, ldInst, prefix, lnClass, lnInst);
            if (ln is null)
                return TypeResolution.Unresolved(fc);

            var lnTypeId = Attr(ln, "lnType");
            if (!_lNodeTypes.TryGetValue(lnTypeId, out var lNodeType))
                return TypeResolution.Unresolved(fc);

            var doSegments = SplitName(doName);
            if (doSegments.Length == 0)
                return TypeResolution.Unresolved(fc);

            var dataObject = lNodeType.Elements().FirstOrDefault(e => Is(e, "DO") && SameName(e, doSegments[0]));
            if (dataObject is null || !_doTypes.TryGetValue(Attr(dataObject, "type"), out var doType))
                return TypeResolution.Unresolved(fc);

            var currentTypeId = Attr(dataObject, "type");
            for (var i = 1; i < doSegments.Length; i++)
            {
                var sdo = doType.Elements().FirstOrDefault(e => Is(e, "SDO") && SameName(e, doSegments[i]));
                if (sdo is null || !_doTypes.TryGetValue(Attr(sdo, "type"), out doType))
                    return TypeResolution.Partial(fc, Attr(doType, "cdc"), currentTypeId);

                currentTypeId = Attr(sdo, "type");
            }

            var cdc = Attr(doType, "cdc");
            var daSegments = SplitName(daName);
            if (daSegments.Length == 0)
                return new TypeResolution(true, fc, cdc, string.Empty, currentTypeId, string.Empty);

            XElement? currentContainer = doType;
            string bType = string.Empty;
            string enumType = string.Empty;
            var resolvedFc = fc;

            for (var i = 0; i < daSegments.Length; i++)
            {
                var child = currentContainer?.Elements().FirstOrDefault(e => (Is(e, "DA") || Is(e, "BDA")) && SameName(e, daSegments[i]));
                if (child is null)
                    return new TypeResolution(false, resolvedFc, cdc, bType, currentTypeId, enumType);

                bType = Attr(child, "bType");
                if (string.IsNullOrWhiteSpace(resolvedFc))
                    resolvedFc = Attr(child, "fc");

                var childType = Attr(child, "type");
                if (string.Equals(bType, "Struct", StringComparison.OrdinalIgnoreCase) && _daTypes.TryGetValue(childType, out var daType))
                {
                    currentContainer = daType;
                    currentTypeId = childType;
                    continue;
                }

                if (string.Equals(bType, "Enum", StringComparison.OrdinalIgnoreCase) && _enumTypeIds.Contains(childType))
                    enumType = childType;

                currentTypeId = childType;
            }

            return new TypeResolution(true, resolvedFc, cdc, bType, currentTypeId, enumType);
        }

        private XElement? FindLogicalNode(string iedName, string ldInst, string prefix, string lnClass, string lnInst)
        {
            if (!_ieds.TryGetValue(iedName, out var ied))
                return null;

            var lDevice = ied.Descendants().FirstOrDefault(e => Is(e, "LDevice") && string.Equals(Attr(e, "inst"), ldInst, StringComparison.OrdinalIgnoreCase));
            if (lDevice is null)
                return null;

            if (string.Equals(lnClass, "LLN0", StringComparison.OrdinalIgnoreCase))
                return lDevice.Elements().FirstOrDefault(e => Is(e, "LN0"));

            return lDevice.Elements().FirstOrDefault(e =>
                Is(e, "LN") &&
                string.Equals(Attr(e, "prefix"), prefix, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Attr(e, "lnClass"), lnClass, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Attr(e, "inst"), lnInst, StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, XElement> BuildElementIndex(IEnumerable<XElement> elements, string keyAttribute)
            => elements
                .Where(e => !string.IsNullOrWhiteSpace(Attr(e, keyAttribute)))
                .GroupBy(e => Attr(e, keyAttribute), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        private static string[] SplitName(string name)
            => (name ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static bool SameName(XElement element, string name)
            => string.Equals(Attr(element, "name"), name, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record TypeResolution(bool Resolved, string Fc, string Cdc, string BType, string TypeId, string EnumType)
    {
        public static TypeResolution Unresolved(string fc)
            => new(false, fc, string.Empty, string.Empty, string.Empty, string.Empty);

        public static TypeResolution Partial(string fc, string cdc, string typeId)
            => new(false, fc, cdc, string.Empty, typeId, string.Empty);
    }
}
