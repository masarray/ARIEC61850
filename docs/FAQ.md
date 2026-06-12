# FAQ

## Is ARIEC61850 based on third-party IEC 61850 stack?

No. ARIEC61850 is intended as a clean-room implementation. External projects may
be studied as behavioral references or interoperability peers, but restrictive-license source
code must not be copied or translated into this repository.

## Does the stack support live Sampled Values publishing?

Yes, the current CLI can import an SCL file, select an SV stream, generate a
demo 4I+4V payload from DataSet order, and publish raw Ethernet frames through a
selected Npcap adapter.

## Is the SV publisher protection-grade?

No. The current publisher is software-paced and intended for lab validation,
tool development, and interoperability smoke tests.

## Does the stack support MMS client workflows?

Yes, partially. The current MMS client can associate with a lab IED, discover
domains, variables, DataSets, and RCBs, build an FC-aware live directory,
resolve and smart-read values, inspect DataSet members, plan report usage, and
run guarded static/dynamic report smoke tests.

It is not a complete MMS stack yet. File transfer, log services, setting groups,
control services, MMS server behavior, full long-running receive pump, and BRCB
recovery are still planned.

## Does it support GOOSE publishing?

Yes. The stack can build GOOSE frames, parse GOOSE frames, build SCL-backed
publisher profiles, and publish bounded or continuous GOOSE traffic through a
selected lab adapter. GOOSE subscriber support is still planned.

## Does it support IEC 61850 reporting?

Partially. Static and dynamic report workflows now have guarded lab smoke
commands. The stack can plan a safe report target, enable reporting, trigger GI,
receive `InformationReport` frames, map values by DataSet order, and clean up.

The remaining work is the production-grade receive pump, full optional-field
decode, BRCB recovery, long-run monitor, and multi-vendor validation.

## Can this be used in a WPF tester?

Yes. The repository is organized so WPF and CLI applications depend on the
stack, while protocol logic remains reusable in `src/`.

## Why is there no NuGet package yet?

The API is still moving. A package should wait until the MMS receive pump,
report monitor, file transfer, and GOOSE/SV subscriber boundaries are more
stable.
