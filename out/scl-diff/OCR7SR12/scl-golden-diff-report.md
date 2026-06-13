# SCL Golden Reference Diff

- Generated: 2026-06-13 11:49:38.788 UTC
- Golden: `OCR7SR12.iid`
- Candidate: `OCR7SR12.standard-discovery.iid`

## Summary

| Area | Golden | Candidate | Missing | Extra |
| --- | ---: | ---: | ---: | ---: |
| Logical devices | 4 | 4 | 0 | 0 |
| Logical nodes | 123 | 123 | 0 | 0 |
| DataSets | 1 | 1 | 0 | 0 |
| Reports | 286 | 286 | 286 | 286 |
| GOOSE control blocks | 0 | 0 | 0 | 0 |
| Sampled Value control blocks | 0 | 0 | 0 | 0 |
| Setting controls | 1 | 1 | 0 | 0 |
| Log controls | 0 | 0 | 0 | 0 |
| LNodeTypes | 42 | 123 | 42 | 123 |
| DOTypes | 34 | 899 | 34 | 500 |
| DATypes | 10 | 524 | 10 | 500 |
| EnumTypes | 13 | 4 | 13 | 4 |

## Service capability differences

| Service | Golden | Candidate |
| --- | --- | --- |
| OCR7SR12.ConfDataSet | max=1,modify=false | missing |
| OCR7SR12.ConfLNs | fixLnInst=true,fixPrefix=true | missing |
| OCR7SR12.ConfReportControl | bufConf=false,max=286 | missing |
| OCR7SR12.DataObjectDirectory | present | missing |
| OCR7SR12.DataSetDirectory | present | missing |
| OCR7SR12.DynAssociation | present | missing |
| OCR7SR12.DynDataSet | max=42 | missing |
| OCR7SR12.FileHandling | present | missing |
| OCR7SR12.GetCBValues | present | missing |
| OCR7SR12.GetDataObjectDefinition | present | missing |
| OCR7SR12.GetDataSetValue | present | missing |
| OCR7SR12.GetDirectory | present | missing |
| OCR7SR12.GOOSE | max=0 | missing |
| OCR7SR12.GSSE | max=0 | missing |
| OCR7SR12.ReadWrite | present | missing |
| OCR7SR12.ReportSettings | bufTime=Dyn,intgPd=Dyn,optFields=Dyn,resvTms=true,rptID=Dyn,trgOps=Dyn | missing |
| OCR7SR12.SetDataSetValue | present | missing |
| OCR7SR12.SettingGroups | present | missing |

## CDC differences

| LNClass.DO | Golden CDC | Candidate CDC | Golden DOType | Candidate DOType |
| --- | --- | --- | --- | --- |
| GGIO.ISCSO1 | INC | ISC | OCR7SR12PROT.A50AFDSARC1.FACntRs | DO_ISC_GGIO_ISCSO1 |
| GGIO.ISCSO2 | INC | ISC | OCR7SR12PROT.CntDelGGIO1.ISCSO2 | DO_ISC_GGIO_ISCSO2 |
| PTTR.AlmThm | ACT | SPS | OCR7SR12PROT.A49PTTR1.AlmThm | DO_SPS_PTTR_AlmThm |

## Type template reuse

| Kind | Golden types | Golden shapes | Candidate types | Candidate shapes | Candidate duplicate shapes |
| --- | ---: | ---: | ---: | ---: | ---: |
| DOType | 34 | 33 | 899 | 87 | 812 |
| DAType | 10 | 9 | 524 | 213 | 311 |

## Reports details

Missing in candidate:
- `OCR7SR12/CTRL/BI6GGIO1.urcbA`
- `OCR7SR12/CTRL/BI6GGIO1.urcbB`
- `OCR7SR12/CTRL/BO8GGIO1.urcbA`
- `OCR7SR12/CTRL/BO8GGIO1.urcbB`
- `OCR7SR12/CTRL/DPDOesGGIO1.urcbA`
- `OCR7SR12/CTRL/DPDOesGGIO1.urcbB`
- `OCR7SR12/CTRL/DPDOesGGIO2.urcbA`
- `OCR7SR12/CTRL/DPDOesGGIO2.urcbB`
- `OCR7SR12/CTRL/DPDOesGGIO3.urcbA`
- `OCR7SR12/CTRL/DPDOesGGIO3.urcbB`
- `OCR7SR12/CTRL/DPDOesGGIO4.urcbA`
- `OCR7SR12/CTRL/DPDOesGGIO4.urcbB`
- `OCR7SR12/CTRL/DPDOnsGGIO1.urcbA`
- `OCR7SR12/CTRL/DPDOnsGGIO1.urcbB`
- `OCR7SR12/CTRL/DPDOnsGGIO2.urcbA`
- `OCR7SR12/CTRL/DPDOnsGGIO2.urcbB`
- `OCR7SR12/CTRL/DPDOnsGGIO3.urcbA`
- `OCR7SR12/CTRL/DPDOnsGGIO3.urcbB`
- `OCR7SR12/CTRL/DPDOnsGGIO4.urcbA`
- `OCR7SR12/CTRL/DPDOnsGGIO4.urcbB`
- `OCR7SR12/CTRL/DPi8GGIO1.urcbA`
- `OCR7SR12/CTRL/DPi8GGIO1.urcbB`
- `OCR7SR12/CTRL/DPi8GGIO2.urcbA`
- `OCR7SR12/CTRL/DPi8GGIO2.urcbB`
- `OCR7SR12/CTRL/DPo8GGIO1.urcbA`
- `OCR7SR12/CTRL/DPo8GGIO1.urcbB`
- `OCR7SR12/CTRL/DPo8GGIO2.urcbA`
- `OCR7SR12/CTRL/DPo8GGIO2.urcbB`
- `OCR7SR12/CTRL/E4GGIO1.urcbA`
- `OCR7SR12/CTRL/E4GGIO1.urcbB`
- `OCR7SR12/CTRL/L9GGIO1.urcbA`
- `OCR7SR12/CTRL/L9GGIO1.urcbB`
- `OCR7SR12/CTRL/LLN0.brcbA`
- `OCR7SR12/CTRL/LLN0.brcbB`
- `OCR7SR12/CTRL/LLN0.urcbA`
- `OCR7SR12/CTRL/LLN0.urcbB`
- `OCR7SR12/CTRL/LLN0.urcbC`
- `OCR7SR12/CTRL/LLN0.urcbD`
- `OCR7SR12/CTRL/LLN0.urcbE`
- `OCR7SR12/CTRL/LLN0.urcbF`
- `OCR7SR12/CTRL/LLN0.urcbG`
- `OCR7SR12/CTRL/LLN0.urcbH`
- `OCR7SR12/CTRL/LLN0.urcbI`
- `OCR7SR12/CTRL/LLN0.urcbJ`
- `OCR7SR12/CTRL/LPHD1.urcbA`
- `OCR7SR12/CTRL/LPHD1.urcbB`
- `OCR7SR12/CTRL/Q0CILO1.urcbA`
- `OCR7SR12/CTRL/Q0CILO1.urcbB`
- `OCR7SR12/CTRL/Q0CSWI1.urcbA`
- `OCR7SR12/CTRL/Q0CSWI1.urcbB`
- `OCR7SR12/CTRL/Q0XCBR1.urcbA`
- `OCR7SR12/CTRL/Q0XCBR1.urcbB`
- `OCR7SR12/CTRL/SPDOesGGIO1.urcbA`
- `OCR7SR12/CTRL/SPDOesGGIO1.urcbB`
- `OCR7SR12/CTRL/SPDOesGGIO2.urcbA`
- `OCR7SR12/CTRL/SPDOesGGIO2.urcbB`
- `OCR7SR12/CTRL/SPDOesGGIO3.urcbA`
- `OCR7SR12/CTRL/SPDOesGGIO3.urcbB`
- `OCR7SR12/CTRL/SPDOesGGIO4.urcbA`
- `OCR7SR12/CTRL/SPDOesGGIO4.urcbB`
- `OCR7SR12/CTRL/SPDOnsGGIO1.urcbA`
- `OCR7SR12/CTRL/SPDOnsGGIO1.urcbB`
- `OCR7SR12/CTRL/SPDOnsGGIO2.urcbA`
- `OCR7SR12/CTRL/SPDOnsGGIO2.urcbB`
- `OCR7SR12/CTRL/SPDOnsGGIO3.urcbA`
- `OCR7SR12/CTRL/SPDOnsGGIO3.urcbB`
- `OCR7SR12/CTRL/SPDOnsGGIO4.urcbA`
- `OCR7SR12/CTRL/SPDOnsGGIO4.urcbB`
- `OCR7SR12/CTRL/SPi64GGIO1.urcbA`
- `OCR7SR12/CTRL/SPi64GGIO1.urcbB`
- `OCR7SR12/CTRL/SPo32GGIO1.urcbA`
- `OCR7SR12/CTRL/SPo32GGIO1.urcbB`
- `OCR7SR12/CTRL/V8GGIO1.urcbA`
- `OCR7SR12/CTRL/V8GGIO1.urcbB`
- `OCR7SR12/DR/LLN0.brcbA`
- `OCR7SR12/DR/LLN0.brcbB`
- `OCR7SR12/DR/LLN0.urcbA`
- `OCR7SR12/DR/LLN0.urcbB`
- `OCR7SR12/DR/LLN0.urcbC`
- `OCR7SR12/DR/LLN0.urcbD`
- ... 206 more item(s)

Extra in candidate:
- `OCR7SR12/CTRL/BI6GGIO1.urcbA01`
- `OCR7SR12/CTRL/BI6GGIO1.urcbB01`
- `OCR7SR12/CTRL/BO8GGIO1.urcbA01`
- `OCR7SR12/CTRL/BO8GGIO1.urcbB01`
- `OCR7SR12/CTRL/DPDOesGGIO1.urcbA01`
- `OCR7SR12/CTRL/DPDOesGGIO1.urcbB01`
- `OCR7SR12/CTRL/DPDOesGGIO2.urcbA01`
- `OCR7SR12/CTRL/DPDOesGGIO2.urcbB01`
- `OCR7SR12/CTRL/DPDOesGGIO3.urcbA01`
- `OCR7SR12/CTRL/DPDOesGGIO3.urcbB01`
- `OCR7SR12/CTRL/DPDOesGGIO4.urcbA01`
- `OCR7SR12/CTRL/DPDOesGGIO4.urcbB01`
- `OCR7SR12/CTRL/DPDOnsGGIO1.urcbA01`
- `OCR7SR12/CTRL/DPDOnsGGIO1.urcbB01`
- `OCR7SR12/CTRL/DPDOnsGGIO2.urcbA01`
- `OCR7SR12/CTRL/DPDOnsGGIO2.urcbB01`
- `OCR7SR12/CTRL/DPDOnsGGIO3.urcbA01`
- `OCR7SR12/CTRL/DPDOnsGGIO3.urcbB01`
- `OCR7SR12/CTRL/DPDOnsGGIO4.urcbA01`
- `OCR7SR12/CTRL/DPDOnsGGIO4.urcbB01`
- `OCR7SR12/CTRL/DPi8GGIO1.urcbA01`
- `OCR7SR12/CTRL/DPi8GGIO1.urcbB01`
- `OCR7SR12/CTRL/DPi8GGIO2.urcbA01`
- `OCR7SR12/CTRL/DPi8GGIO2.urcbB01`
- `OCR7SR12/CTRL/DPo8GGIO1.urcbA01`
- `OCR7SR12/CTRL/DPo8GGIO1.urcbB01`
- `OCR7SR12/CTRL/DPo8GGIO2.urcbA01`
- `OCR7SR12/CTRL/DPo8GGIO2.urcbB01`
- `OCR7SR12/CTRL/E4GGIO1.urcbA01`
- `OCR7SR12/CTRL/E4GGIO1.urcbB01`
- `OCR7SR12/CTRL/L9GGIO1.urcbA01`
- `OCR7SR12/CTRL/L9GGIO1.urcbB01`
- `OCR7SR12/CTRL/LLN0.brcbA01`
- `OCR7SR12/CTRL/LLN0.brcbB01`
- `OCR7SR12/CTRL/LLN0.urcbA01`
- `OCR7SR12/CTRL/LLN0.urcbB01`
- `OCR7SR12/CTRL/LLN0.urcbC01`
- `OCR7SR12/CTRL/LLN0.urcbD01`
- `OCR7SR12/CTRL/LLN0.urcbE01`
- `OCR7SR12/CTRL/LLN0.urcbF01`
- `OCR7SR12/CTRL/LLN0.urcbG01`
- `OCR7SR12/CTRL/LLN0.urcbH01`
- `OCR7SR12/CTRL/LLN0.urcbI01`
- `OCR7SR12/CTRL/LLN0.urcbJ01`
- `OCR7SR12/CTRL/LPHD1.urcbA01`
- `OCR7SR12/CTRL/LPHD1.urcbB01`
- `OCR7SR12/CTRL/Q0CILO1.urcbA01`
- `OCR7SR12/CTRL/Q0CILO1.urcbB01`
- `OCR7SR12/CTRL/Q0CSWI1.urcbA01`
- `OCR7SR12/CTRL/Q0CSWI1.urcbB01`
- `OCR7SR12/CTRL/Q0XCBR1.urcbA01`
- `OCR7SR12/CTRL/Q0XCBR1.urcbB01`
- `OCR7SR12/CTRL/SPDOesGGIO1.urcbA01`
- `OCR7SR12/CTRL/SPDOesGGIO1.urcbB01`
- `OCR7SR12/CTRL/SPDOesGGIO2.urcbA01`
- `OCR7SR12/CTRL/SPDOesGGIO2.urcbB01`
- `OCR7SR12/CTRL/SPDOesGGIO3.urcbA01`
- `OCR7SR12/CTRL/SPDOesGGIO3.urcbB01`
- `OCR7SR12/CTRL/SPDOesGGIO4.urcbA01`
- `OCR7SR12/CTRL/SPDOesGGIO4.urcbB01`
- `OCR7SR12/CTRL/SPDOnsGGIO1.urcbA01`
- `OCR7SR12/CTRL/SPDOnsGGIO1.urcbB01`
- `OCR7SR12/CTRL/SPDOnsGGIO2.urcbA01`
- `OCR7SR12/CTRL/SPDOnsGGIO2.urcbB01`
- `OCR7SR12/CTRL/SPDOnsGGIO3.urcbA01`
- `OCR7SR12/CTRL/SPDOnsGGIO3.urcbB01`
- `OCR7SR12/CTRL/SPDOnsGGIO4.urcbA01`
- `OCR7SR12/CTRL/SPDOnsGGIO4.urcbB01`
- `OCR7SR12/CTRL/SPi64GGIO1.urcbA01`
- `OCR7SR12/CTRL/SPi64GGIO1.urcbB01`
- `OCR7SR12/CTRL/SPo32GGIO1.urcbA01`
- `OCR7SR12/CTRL/SPo32GGIO1.urcbB01`
- `OCR7SR12/CTRL/V8GGIO1.urcbA01`
- `OCR7SR12/CTRL/V8GGIO1.urcbB01`
- `OCR7SR12/DR/LLN0.brcbA01`
- `OCR7SR12/DR/LLN0.brcbB01`
- `OCR7SR12/DR/LLN0.urcbA01`
- `OCR7SR12/DR/LLN0.urcbB01`
- `OCR7SR12/DR/LLN0.urcbC01`
- `OCR7SR12/DR/LLN0.urcbD01`
- ... 206 more item(s)

## Reading the report

- Missing/extra control blocks or datasets are structural gaps that should be prioritized before cosmetic SCL cleanup.
- CDC differences show where ARIEC61850 reconstruction diverges from the golden engineering model.
- High duplicate-shape counts indicate missing type-template deduplication; this is one reason generated SCL can be much larger than IEDScout export.
- This report intentionally compares engineering SCL structure; it does not prove that every object is readable via MMS.
