# G2.4 Transactional URCB Proof-Field Lease

Field evidence from SIPROTEC `AA1C1F08R4` showed 30 live-proven empty/free URCBs. Their current report-control bit strings are `TrgOps=0204` plus vendor padding variants and `OptFlds=060000`.

## P0 correction — TrgOps reserved bit

The IEC 61850 MMS mapping for the six-bit TrgOps field reserves bit 0. The five standard report triggers occupy bits 1..5:

- bit 0: reserved
- bit 1: dchg
- bit 2: qchg
- bit 3: dupd
- bit 4: integrity/period
- bit 5: GI

Therefore the physical value `0204` is GI-only. Correct canonical values include `0204` GI-only, `0240` dchg-only, `0244` dchg+GI, and `027C` all five standard triggers.

## BIT STRING restore semantics

TrgOps has six significant bits and two trailing unused bits. OptFlds has ten significant bits and six trailing unused bits in the final payload octet. Raw BER evidence is retained, while commissioning success compares the significant IEC value. Significant-bit differences remain hard failures.

## P0 isolated TrgOps micro-probe — physical PASS

Physical P0 PASS on `AA1C1F08R4ADD/LLN0.RP.A_URCB01` proved original `0207`, requested/readback `0244`, exact restore `0207`, and a healthy auxiliary association. P0 never touches OptFlds, DatSet, Resv, RptEna, GI, dynamic DataSet services, report routing, or qualification profile state.

## P1 isolated OptFlds micro-probe

After P0 physical PASS, P1 performs only:

1. choose one forced-live proven-empty/free URCB;
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

1. URCB only; BRCB is excluded from first active proof.
2. Caller must already prove DatSet empty, RptEna=false, reservation/Owner free.
3. Capture original TrgOps and OptFlds before mutation.
4. Significant-bit readback is mandatory after temporary writes.
5. Partial preparation failure must rollback.
6. Actual correctly mapped InformationReport remains mandatory for G2.4 success.
7. RptEna/DataSet/Resv cleanup must complete before proof-field restoration.
8. Restore original OptFlds then TrgOps and prove readback.
9. No profile advancement if any proof or cleanup gate fails.
10. Production automatic dynamic reporting remains OFF.
