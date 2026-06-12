# FAQ

## Is ARIEC61850 based on libIEC61850?

No. ARIEC61850 is intended as a clean-room implementation. External projects may
be studied as behavioral references or interoperability peers, but GPL source
code must not be copied or translated into this repository.

## Does the stack support live Sampled Values publishing?

Yes, the current CLI can import an SCL file, select an SV stream, generate a
demo 4I+4V payload from DataSet order, and publish raw Ethernet frames through a
selected Npcap adapter.

## Is the SV publisher protection-grade?

No. The current publisher is software-paced and intended for lab validation,
tool development, and interoperability smoke tests.

## Does the stack support MMS client discovery?

Not yet. MMS transport layers are on the roadmap after process-bus publisher and
subscriber services are stable.

## Does it support GOOSE publishing?

The stack can build GOOSE frames and SCL-backed GOOSE publisher profiles. The
next step is a reusable retransmission schedule and live publish command.

## Can this be used in a WPF tester?

Yes. The repository is organized so WPF and CLI applications depend on the
stack, while protocol logic remains reusable in `src/`.

## Why is there no NuGet package yet?

The API is still moving. A package should wait until SCL, SV/GOOSE publisher and
subscriber boundaries, and MMS transport foundations are stable.
