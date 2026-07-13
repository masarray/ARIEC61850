# Close-edge event reporting verification

Field verification after merge:

1. Arm dynamic monitoring and confirm the selected RCB write log includes `TrgOps` with `dchg` before `RptEna=true`.
2. Alternate Open and Close commands while watching IEDScout and ArIED Live Monitor.
3. Both directions must update from the report path without waiting for MMS validation fallback.
4. The warning `value change not delivered by the armed report` must not recur for the position point.
5. If the IED rejects `TrgOps`, monitoring must report the configuration failure instead of claiming event-driven acquisition.
