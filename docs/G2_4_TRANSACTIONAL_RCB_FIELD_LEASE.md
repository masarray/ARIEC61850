# G2.4 Transactional URCB Proof-Field Lease

Field evidence from SIPROTEC `AA1C1F08R4` showed 30 live-proven empty/free URCBs, but their current report-control bit strings are `TrgOps=0204` and `OptFlds=060000`.

`0204` is the exact MMS BitString display form: unused-bits octet `02`, payload `04`. For a six-bit TrgOps field this means application-trigger is set while GI is not. `060000` is unused-bits octet `06` with a zero 10-bit OptFlds payload, so DataSetName/ReasonForInclusion are not enabled.

G2.4 therefore must not classify the IED as lacking dynamic URCB capacity. Instead, explicit commissioning may temporarily configure exactly one already-proven-free URCB with self-identifying proof fields, provided the exact original MMS values are captured and restored with exact readback.

## Safety contract

1. URCB only; BRCB is excluded from first proof.
2. Caller must already prove DatSet empty, RptEna=false, reservation/Owner free.
3. Capture original TrgOps and OptFlds as exact MMS BitString values before any field mutation.
4. Apply temporary proof configuration only on the explicit commissioning association.
5. Exact readback is required after each temporary field write.
6. Any partial preparation failure immediately attempts exact rollback.
7. After report proof, RptEna/DataSet/Resv cleanup must occur before restoring proof fields.
8. Restore OptFlds then TrgOps to their exact original MMS values and verify exact readback.
9. Profile advancement remains prohibited if any cleanup/restore step is not proven.
10. Production automatic dynamic reporting remains off.

The engine also recognizes the exact raw display form produced by `MmsDataCodec.ToDisplayString` for RCB BitStrings, e.g. `0204`, `0288`, `060000`, and `061800`. Parsing is shape- and unused-bit-count-validated so arbitrary text is not reinterpreted as control flags.
