# G2.4 Transactional URCB Proof-Field Lease

Field evidence from SIPROTEC `AA1C1F08R4` showed 30 live-proven empty/free URCBs. Their current report-control bit strings are `TrgOps=0204` plus vendor padding variants and `OptFlds=060000`.

## P0 correction — TrgOps reserved bit

A physical write exposed an older codec defect. The IEC 61850 MMS mapping for the six-bit TrgOps field reserves bit 0. The five standard report triggers occupy bits 1..5:

- bit 0: reserved
- bit 1: dchg
- bit 2: qchg
- bit 3: dupd
- bit 4: integrity/period
- bit 5: GI

Therefore the physical value `0204` is GI-only. The previous G2.4 candidate incorrectly encoded `dchg+GI` as `0288`. With the corrected reserved-bit mapping:

- GI only -> `0204`
- dchg only -> `0240`
- dchg + GI -> `0244`
- dchg + qchg + dupd + integrity + GI -> `027C`

The physical `0288 -> 0208` observation is consequently not evidence that the relay rejects dchg. `0288` had the reserved bit set and the integrity bit set; it was not a valid dchg+GI request.

## BIT STRING restore semantics

The first octet in the display form is the MMS BIT STRING unused-bit count. TrgOps has six significant bits, so two trailing payload bits are unused. OptFlds has ten significant bits, so six trailing bits in the last payload octet are unused.

Raw BER evidence is always retained, but commissioning success compares only the significant IEC bits. A significant-bit difference remains a hard mismatch.

## P0 isolated TrgOps micro-probe — physical PASS

P0 uses exactly one forced-live proven-empty/free URCB and performs only:

1. direct-read and retain original TrgOps MMS BitString;
2. encode corrected `dchg+GI` as canonical `0244`;
3. write TrgOps only;
4. direct-read and compare significant bits while retaining raw BER evidence;
5. in `finally`, write the exact captured original TrgOps value back;
6. direct-read restore state and require significant-bit equality.

Physical P0 PASS on `AA1C1F08R4ADD/LLN0.RP.A_URCB01` proved:

- original `0207`;
- requested `0244`;
- live readback `0244`;
- requested semantic and raw equality PASS;
- restore live readback `0207`;
- restore semantic and raw equality PASS;
- association remained healthy.

The P0 micro-probe never writes DatSet, OptFlds, Resv, RptEna, GI, and never creates/deletes a DataSet. It does not change production monitoring or qualification profile state.

## P1 isolated OptFlds micro-probe

After P0 physical PASS, the next proof-field gate is deliberately isolated from TrgOps and report activation. P1 performs only:

1. caller selects one forced-live proven-empty/free URCB;
2. direct-read and retain original OptFlds MMS BitString;
3. encode `reason-for-inclusion + data-set-name` as canonical `061800`;
4. write OptFlds only;
5. direct-read and require ten-bit significant-value equality;
6. in `finally`, write the exact captured original OptFlds MMS value back;
7. direct-read restore state and require significant-bit equality;
8. retain raw BER, semantic equality, raw equality, and padding-only-difference evidence separately.

P1 never writes TrgOps, DatSet, Resv, RptEna, GI, never defines/deletes a DataSet, never starts a report monitor, and never changes qualification profile state.

Only after P0 TrgOps and P1 OptFlds are both physically proven may the broader one-URCB G2.4 InformationReport proof be retried.

## G2.4 safety contract

1. URCB only; BRCB is excluded from first proof.
2. Caller must already prove DatSet empty, RptEna=false, reservation/Owner free.
3. Capture original TrgOps and OptFlds as MMS BitString values before any field mutation.
4. Apply temporary proof configuration only on the explicit commissioning association.
5. Significant-bit readback is required after each temporary field write; raw BER equality is recorded separately.
6. Any partial preparation failure immediately attempts rollback.
7. After actual report proof, RptEna/DataSet/Resv cleanup must occur before restoring proof fields.
8. Restore OptFlds then TrgOps using captured original MMS values and verify significant-bit readback.
9. Profile advancement remains prohibited if actual InformationReport proof or any cleanup/restore step is not proven.
10. Production automatic dynamic reporting remains off.

The engine recognizes the raw display form produced by `MmsDataCodec.ToDisplayString` for RCB BitStrings and validates shape plus unused-bit count before interpreting control flags.
