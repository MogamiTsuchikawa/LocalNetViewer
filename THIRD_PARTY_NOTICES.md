# Third-Party Notices

## IEEE Registration Authority public listings

`src/LocalNetViewer.Platform/Data/oui.tsv` is generated from the public
MAC Address Block Large, Medium, and Small assignment CSV listings published by
the IEEE Registration Authority.

The file is included for local MAC vendor lookup and contains normalized
hexadecimal assignment prefixes mapped to organization names:

```text
<hex-prefix>\t<organization-name>
```

Prefix lengths in this file:

- 6 hex characters: MA-L / OUI
- 7 hex characters: MA-M
- 9 hex characters: MA-S / OUI-36

This third-party registry data is not authored by the LocalNetViewer project
and is not relicensed under the MIT License by this repository.

Sources:

- https://standards.ieee.org/products-programs/regauth/
- https://standards-oui.ieee.org/oui/oui.csv
- https://standards-oui.ieee.org/oui28/mam.csv
- https://standards-oui.ieee.org/oui36/oui36.csv
