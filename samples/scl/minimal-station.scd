<?xml version="1.0" encoding="UTF-8"?>
<SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
  <Header id="AR_MINIMAL_STATION" version="1" revision="A" />
  <Communication>
    <SubNetwork name="ProcessBus" type="8-MMS">
      <ConnectedAP iedName="MU01" apName="P1">
        <SMV ldInst="LD0" cbName="MSVCB01">
          <Address>
            <P type="MAC-Address">01-0C-CD-04-00-01</P>
            <P type="APPID">4001</P>
            <P type="VLAN-ID">200</P>
            <P type="VLAN-PRIORITY">4</P>
          </Address>
        </SMV>
        <GSE ldInst="LD0" cbName="GCB01">
          <Address>
            <P type="MAC-Address">01-0C-CD-01-00-01</P>
            <P type="APPID">1001</P>
            <P type="VLAN-ID">100</P>
            <P type="VLAN-PRIORITY">4</P>
          </Address>
        </GSE>
      </ConnectedAP>
    </SubNetwork>
  </Communication>
  <IED name="MU01" manufacturer="AR" type="MergingUnit" configVersion="1">
    <AccessPoint name="P1">
      <Server>
        <LDevice inst="LD0">
          <LN0 lnClass="LLN0" lnType="LLN0Type">
            <DataSet name="dsSV">
              <FCDA ldInst="LD0" lnClass="TCTR" lnInst="1" doName="Amp" daName="instMag.i" fc="MX" />
              <FCDA ldInst="LD0" lnClass="TCTR" lnInst="1" doName="Amp" daName="q" fc="MX" />
            </DataSet>
            <DataSet name="dsGO">
              <FCDA ldInst="LD0" lnClass="XCBR" lnInst="1" doName="Pos" daName="stVal" fc="ST" />
              <FCDA ldInst="LD0" lnClass="XCBR" lnInst="1" doName="Pos" daName="q" fc="ST" />
              <FCDA ldInst="LD0" lnClass="XCBR" lnInst="1" doName="Pos" daName="t" fc="ST" />
            </DataSet>
            <SampledValueControl name="MSVCB01" datSet="dsSV" smvID="MU01LD0/LLN0$MSVCB01" confRev="3" smpRate="4000" nofASDU="1" smpMod="SmpPerSec" multicast="true" />
            <GSEControl name="GCB01" datSet="dsGO" appID="trip-goose" confRev="2" type="GOOSE" minTime="4" maxTime="1000" />
            <ReportControl name="URCB01" datSet="dsGO" rptID="MU01LD0/LLN0$RP$URCB01" confRev="1" buffered="false" bufTime="100" intgPd="1000" />
          </LN0>
          <LN lnClass="TCTR" inst="1" lnType="TCTRType" />
          <LN lnClass="XCBR" inst="1" lnType="XCBRType" />
        </LDevice>
      </Server>
    </AccessPoint>
  </IED>
  <DataTypeTemplates>
    <LNodeType id="LLN0Type" lnClass="LLN0" />
    <LNodeType id="TCTRType" lnClass="TCTR">
      <DO name="Amp" type="AmpType" />
    </LNodeType>
    <DOType id="AmpType" cdc="SAV">
      <DA name="instMag" bType="Struct" type="AnalogueValue" fc="MX" />
      <DA name="q" bType="Quality" fc="MX" />
    </DOType>
    <DAType id="AnalogueValue">
      <BDA name="i" bType="INT32" />
    </DAType>
    <LNodeType id="XCBRType" lnClass="XCBR">
      <DO name="Pos" type="PosType" />
    </LNodeType>
    <DOType id="PosType" cdc="DPC">
      <DA name="stVal" bType="BOOLEAN" fc="ST" />
      <DA name="q" bType="Quality" fc="ST" />
      <DA name="t" bType="Timestamp" fc="ST" />
    </DOType>
  </DataTypeTemplates>
</SCL>
