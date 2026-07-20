# COTP FileRead response handling

A relay may split one MMS FileRead response across many COTP Data TPDUs. The client reads until EOT and limits the total reassembled response size. This supports multi-megabyte fault records without relying on a small fixed fragment count.