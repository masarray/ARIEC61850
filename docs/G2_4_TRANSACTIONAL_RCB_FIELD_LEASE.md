# G2.4 Transactional URCB Proof-Field Lease

Field evidence from SIPROTEC `AA1C1F08R4` showed 30 live-proven empty/free URCBs. Their current report-control bit strings are `TrgOps=0204` and `OptFlds=060000`.

## P0 correction — TrgOps reserved bit

A later physical write exposed an older codec defect. The IEC 61850 MMS mapping for the six-bit TrgOps field reserves bit 0. The five standard report triggers occupy bits 1..5:

- bit 0: reserved
- bit 1: dchg
- bit 2: qchg
- bit 3: dupd
- bit 4: integrity/period
- bit 5: GI

This matches the established libIEC61850 client behavior, which reads TrgOps bits 1..5 and writes the five-bit API trigger mask shifted left by one for the MMS BIT STRING.

Therefore the physical value `0204` is GI-only, not application-trigger. The previous G2.4 candidate incorrectly encoded `dchg+GI` as `0288`. With the corrected reserved-bit mapping:

- GI only -> `0204`
- dchg only -> `0240`
- dchg + GI -> `0244`
- dchg + qchg + dupd + integrity + GI -> `027C`

The physical `0288 -> 0208` observation is consequently not evidence that the relay rejects dchg. `0288` had the reserved bit set and the integrity bit set; it was not a valid dchg+GI request.

## BIT STRING restore semantics

The first octet in the display form is the MMS BIT STRING unused-bit count. TrgOps has six significant bits, so two trailing payload bits are unused. OptFlds has ten significant bits, so six trailing bits in the last payload octet are unused.

Raw BER evidence is always retained, but commissioning success compares only the significant IEC bits. This means a relay readback such as `0207` is semantically equal to original `0204`: both have unused-bits count `02` and identical six significant TrgOps bits; only the two declared padding bits differ. A significant-bit difference such as `0204` versus `0244` remains a hard mismatch.

## P0 isolated TrgOps micro-probe

Before G2.4 is allowed to touch OptFlds, a dynamic DataSet, reservation, RptEna, or GI, the engine exposes an explicit one-URCB micro-probe:

1. caller selects one forced-live proven-empty/free URCB;
2. direct-read and retain original TrgOps MMS BitString;
3. encode the requested trigger set using the corrected reserved-bit mapping;
4. write TrgOps only;
5. direct-read and compare significant bits while retaining raw BER evidence;
6. in `finally`, write the exact captured original TrgOps value back;
7. direct-read restore state and require significant-bit equality;
8. return success only when requested readback and restore both pass.

The P0 micro-probe never writes DatSet, OptFlds, Resv, RptEna, GI, and never creates/deletes a DataSet. It does not change production monitoring or qualification profile state.

## G2.4 safety contract

1. URCB only; BRCB is excluded from first proof.
2. Caller must already prove DatSet empty, RptEna=false, reservation/Owner free.
3. Capture original TrgOps and OptFlds as MMS BitString values before any field mutation.
4. Apply temporary proof configuration only on the explicit commissioning association.
5. Significant-bit readback is required after each temporary field write; raw BER equality is recorded separately.
6. Any partial preparation failure immediately attempts rollback.
7. After report proof, RptEna/DataSet/Resv cleanup must occur before restoring proof fields.
8. Restore OptFlds then TrgOps using the captured original MMS values and verify significant-bit readback.
9. Profile advancement remains prohibited if any cleanup/restore step is not proven.
10. Production automatic dynamic reporting remains off.

The engine recognizes the raw display form produced by `MmsDataCodec.ToDisplayString` for RCB BitStrings and validates shape plus unused-bit count before interpreting control flags.
