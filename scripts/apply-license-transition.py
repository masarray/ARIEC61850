from __future__ import annotations

import json
import os
import re
import subprocess
import urllib.request
from pathlib import Path
from typing import Iterable

ROOT = Path(__file__).resolve().parents[1]
REPO_FULL_NAME = os.environ.get("GITHUB_REPOSITORY", "masarray/ARIEC61850")
REPO_NAME = REPO_FULL_NAME.split("/")[-1]
REPO_KIND = os.environ.get("REPO_KIND", "engine")
OWNER_NAME = "Mas Ari / masarray"
OWNER_LOGIN = "masarray"
EFFECTIVE_DATE = "2026-07-14"
APACHE_BASE_SHA = os.environ.get("APACHE_BASE_SHA", "d61a83f5b04e7bd2b847174eeac7f4f6e81ee8e1")
GPL_URL = "https://www.gnu.org/licenses/gpl-3.0.txt"
SELF_PATHS = [
    ROOT / "scripts" / "apply-license-transition.py",
    ROOT / ".github" / "workflows" / "apply-gpl-commercial-license.yml",
]


def run(*args: str) -> str:
    return subprocess.check_output(args, cwd=ROOT, text=True, encoding="utf-8", errors="replace").strip()


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig") if path.exists() else ""


def write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text.rstrip() + "\n", encoding="utf-8", newline="\n")


def replace_markdown_section(text: str, heading: str, body: str) -> str:
    pattern = re.compile(rf"(?ms)^## {re.escape(heading)}\s*\n.*?(?=^## |\Z)")
    replacement = f"## {heading}\n\n{body.strip()}\n\n"
    if pattern.search(text):
        return pattern.sub(replacement, text, count=1).rstrip() + "\n"
    return text.rstrip() + "\n\n" + replacement


def append_section_once(path: Path, heading: str, body: str) -> None:
    text = read(path)
    text = replace_markdown_section(text, heading, body)
    write(path, text)


def download_gpl() -> str:
    request = urllib.request.Request(GPL_URL, headers={"User-Agent": "ARIEC61850-license-transition/1.0"})
    with urllib.request.urlopen(request, timeout=30) as response:
        data = response.read().decode("utf-8")
    required = [
        "GNU GENERAL PUBLIC LICENSE",
        "Version 3, 29 June 2007",
        "END OF TERMS AND CONDITIONS",
        "Everyone is permitted to copy and distribute verbatim copies",
    ]
    if any(marker not in data for marker in required) or len(data) < 30000:
        raise RuntimeError("Downloaded GPL text failed integrity/shape validation.")
    return data


def relabel_text_files() -> None:
    excluded = {
        "LICENSE",
        "LICENSE-APACHE-2.0",
        "THIRD_PARTY_NOTICES.md",
        "NOTICE",
        "docs/LICENSING.md",
        f"docs/LICENSE_AUDIT_{EFFECTIVE_DATE}.md",
        "COMMERCIAL-LICENSE.md",
        "CONTRIBUTOR-LICENSE-AGREEMENT.md",
    }
    allowed_suffixes = {
        ".md", ".html", ".htm", ".csproj", ".props", ".targets",
        ".yml", ".yaml", ".json", ".xml", ".cs", ".ps1", ".cmd",
    }
    for path in ROOT.rglob("*"):
        if not path.is_file() or ".git" in path.parts or path.relative_to(ROOT).as_posix() in excluded:
            continue
        if path.suffix.lower() not in allowed_suffixes:
            continue
        try:
            text = read(path)
        except UnicodeDecodeError:
            continue
        updated = text
        updated = updated.replace("license-Apache--2.0-blue", "license-GPL--3.0--or--later-blue")
        updated = updated.replace("Apache License 2.0", "GNU General Public License v3.0 or later")
        updated = updated.replace("Apache-2.0", "GPL-3.0-or-later")
        if updated != text:
            write(path, updated)


def ensure_msbuild_license_metadata() -> None:
    candidates = [ROOT / "Directory.Build.props", *ROOT.rglob("*.csproj")]
    for path in candidates:
        if not path.exists():
            continue
        text = read(path)
        text = re.sub(
            r"<PackageLicenseExpression>.*?</PackageLicenseExpression>",
            "<PackageLicenseExpression>GPL-3.0-or-later</PackageLicenseExpression>",
            text,
            flags=re.I,
        )
        if "<PackageLicenseExpression>" not in text:
            text = re.sub(
                r"(<PropertyGroup(?:\s[^>]*)?>)",
                r"\1\n    <PackageLicenseExpression>GPL-3.0-or-later</PackageLicenseExpression>",
                text,
                count=1,
            )
        if "<Copyright>" not in text:
            text = re.sub(
                r"(<PropertyGroup(?:\s[^>]*)?>)",
                rf"\1\n    <Copyright>Copyright (C) 2026 {OWNER_NAME}</Copyright>",
                text,
                count=1,
            )
        write(path, text)


def patch_readme() -> None:
    path = ROOT / "README.md"
    text = read(path)
    banner = (
        "> **License:** GPL-3.0-or-later for the public community edition. "
        "A separate commercial license is available for proprietary integration, OEM/white-label distribution, "
        "and contractual support. See [Licensing](docs/LICENSING.md)."
    )
    if "**License:** GPL-3.0-or-later" not in text:
        # Put the licensing boundary near the top, after the badge/intro area.
        first_heading_end = text.find("\n## ")
        if first_heading_end >= 0:
            text = text[:first_heading_end].rstrip() + "\n\n" + banner + "\n" + text[first_heading_end:]
        else:
            text = text.rstrip() + "\n\n" + banner + "\n"
    body = f"""
The public community edition is licensed under the **GNU General Public License v3.0 or later** (`GPL-3.0-or-later`). See [LICENSE](LICENSE).

A separate negotiated commercial license is available from the copyright holder for proprietary integration, OEM or white-label distribution, closed-source redistribution, warranty, maintenance, and priority engineering support. See [COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md).

The names, logos, icons, and official-release branding are not granted under the software license. See [TRADEMARK.md](TRADEMARK.md).

Revisions through `{APACHE_BASE_SHA}` remain available under Apache-2.0 on branch [`archive/apache-2.0-final`](../../tree/archive/apache-2.0-final). The former license is preserved in [LICENSE-APACHE-2.0](LICENSE-APACHE-2.0).
"""
    text = replace_markdown_section(text, "License", body)
    write(path, text)


def patch_notice_and_third_party() -> None:
    notice_path = ROOT / "NOTICE"
    old_notice = read(notice_path).strip()
    notice = f"""{REPO_NAME}
Copyright (C) 2026 {OWNER_NAME}

Current public license: GNU General Public License v3.0 or later.
Commercial licensing may be obtained separately from the copyright holder.

License transition effective {EFFECTIVE_DATE}:
- Last Apache-2.0 revision: {APACHE_BASE_SHA}
- Historical branch: archive/apache-2.0-final
- Historical license copy: LICENSE-APACHE-2.0

Third-party components and their notices remain governed by their own licenses; see THIRD_PARTY_NOTICES.md.
"""
    if old_notice and old_notice not in notice:
        notice += "\nHistorical NOTICE text retained for attribution:\n\n" + old_notice + "\n"
    write(notice_path, notice)

    third_party = ROOT / "THIRD_PARTY_NOTICES.md"
    existing = read(third_party).strip()
    intro = """# Third-Party Notices

The project license transition to GPL-3.0-or-later does **not** change the license of any third-party package, tool, sample, or asset. Each third-party component remains subject to its own license and attribution terms.
"""
    if existing:
        existing = re.sub(r"^# Third-Party Notices\s*", "", existing, flags=re.I)
        intro += "\n" + existing
    write(third_party, intro)


def patch_contributing() -> None:
    path = ROOT / "CONTRIBUTING.md"
    if not path.exists():
        write(path, "# Contributing\n")
    body = """
The public project is distributed under `GPL-3.0-or-later` and also maintains a separate commercial-licensing path.

Before a code contribution can be merged, the contributor must:

1. have the legal right to submit the contribution;
2. agree to [CONTRIBUTOR-LICENSE-AGREEMENT.md](CONTRIBUTOR-LICENSE-AGREEMENT.md), which preserves the maintainer's ability to offer both GPL and commercial licensing;
3. add a Developer Certificate of Origin sign-off (`Signed-off-by: Name <email>`) to every commit; and
4. avoid confidential customer data, employer-owned material, vendor source code, restrictive-license code, and mechanically translated proprietary implementations.

Organizational contributions must be submitted by a person authorized to bind the organization. A pull request without the CLA affirmation and DCO sign-off will not be merged.
"""
    append_section_once(path, "Contribution licensing and provenance", body)


def patch_release_scripts() -> None:
    marker = "# ARIEC_LEGAL_FILES"
    snippet = r'''
# ARIEC_LEGAL_FILES: include licensing and attribution documents in distributed packages.
$legalFiles = @("LICENSE", "LICENSE-APACHE-2.0", "COMMERCIAL-LICENSE.md", "TRADEMARK.md", "COPYRIGHT.md", "THIRD_PARTY_NOTICES.md", "NOTICE")
foreach ($legalFile in $legalFiles) {
    $sourceLegalFile = Join-Path $root $legalFile
    if (Test-Path $sourceLegalFile) {
        Copy-Item $sourceLegalFile (Join-Path $publishDir $legalFile) -Force
    }
}
'''.strip()
    for path in ROOT.rglob("*.ps1"):
        text = read(path)
        if "Compress-Archive" not in text or marker in text:
            continue
        text = text.replace("Compress-Archive", snippet + "\n\nCompress-Archive", 1)
        write(path, text)


def collect_git_audit() -> dict:
    commits = run("git", "rev-list", "--count", "HEAD")
    author_lines = run("git", "log", "--format=%aN <%aE>").splitlines()
    authors = sorted(set(line.strip() for line in author_lines if line.strip()), key=str.lower)
    trailers = run("git", "log", "--format=%B").splitlines()
    coauthors = sorted({line.strip() for line in trailers if line.lower().startswith("co-authored-by:")}, key=str.lower)

    pr_authors: list[str] = []
    token = os.environ.get("GITHUB_TOKEN", "")
    if token and REPO_FULL_NAME:
        page = 1
        while page <= 10:
            url = f"https://api.github.com/repos/{REPO_FULL_NAME}/pulls?state=all&per_page=100&page={page}"
            request = urllib.request.Request(url, headers={
                "Authorization": f"Bearer {token}",
                "Accept": "application/vnd.github+json",
                "User-Agent": "license-audit/1.0",
            })
            with urllib.request.urlopen(request, timeout=30) as response:
                items = json.loads(response.read().decode("utf-8"))
            if not items:
                break
            pr_authors.extend(item.get("user", {}).get("login", "") for item in items)
            if len(items) < 100:
                break
            page += 1
    pr_authors = sorted(set(x for x in pr_authors if x), key=str.lower)

    package_refs: list[str] = []
    package_pattern = re.compile(r'<PackageReference\s+Include="([^"]+)"(?:\s+Version="([^"]+)")?', re.I)
    for path in ROOT.rglob("*.csproj"):
        for name, version in package_pattern.findall(read(path)):
            package_refs.append(f"{name} {version or '(version inherited)'} — `{path.relative_to(ROOT).as_posix()}`")
    package_refs = sorted(set(package_refs), key=str.lower)

    tracked = run("git", "ls-files").splitlines()
    binary_exts = {".dll", ".exe", ".pdb", ".so", ".dylib", ".jar", ".zip", ".7z", ".pcap", ".pcapng"}
    binaries = sorted(p for p in tracked if Path(p).suffix.lower() in binary_exts)

    def bot_identity(value: str) -> bool:
        lower = value.lower()
        return any(token in lower for token in ("[bot]", "dependabot", "github-actions", "noreply.github.com"))

    human_authors = [a for a in authors if not bot_identity(a)]
    bot_authors = [a for a in authors if bot_identity(a)]
    external_pr_authors = [a for a in pr_authors if a.lower() != OWNER_LOGIN and "dependabot" not in a.lower() and "bot" not in a.lower()]
    return {
        "commits": commits,
        "authors": authors,
        "human_authors": human_authors,
        "bot_authors": bot_authors,
        "coauthors": coauthors,
        "pr_authors": pr_authors,
        "external_pr_authors": external_pr_authors,
        "package_refs": package_refs,
        "binaries": binaries,
    }


def write_legal_docs(audit: dict) -> None:
    commercial = f"""# Commercial Licensing

{REPO_NAME} is publicly available under the GNU General Public License v3.0 or later (`GPL-3.0-or-later`).

A **separate negotiated commercial license** may be obtained from the copyright holder for organizations that need rights or contractual terms not supplied by the GPL, including:

- proprietary or closed-source integration;
- OEM and white-label distribution;
- redistribution without applying GPL terms to the combined proprietary product;
- private product branches delivered under commercial terms;
- warranty, maintenance, priority support, training, or engineering services.

This document is an invitation to discuss commercial terms; it is **not** itself a commercial license and grants no additional rights.

Contact the project owner through the `masarray` GitHub profile or the repository issue tracker. Do not post confidential technical or commercial information in a public issue.

Commercial licensing can cover only rights controlled by the relevant copyright holder. Third-party components remain subject to their own licenses.
"""
    write(ROOT / "COMMERCIAL-LICENSE.md", commercial)

    trademark = f"""# Trademark and Official Branding Policy

The software license covers source code and other copyrightable project material. It does not grant permission to use the **{REPO_NAME}**, **ARIEC61850**, or **ArIED 61850** names, logos, icons, official-release badges, or other project branding in a way that suggests sponsorship, certification, or official status.

Permitted without separate permission:

- truthful nominative references such as “based on ARIEC61850”;
- links to the official repository;
- unmodified screenshots used for review, education, or compatibility discussion.

Permission is required to:

- distribute a modified product using the official name or logo as its primary branding;
- describe a fork as “official”, “certified”, or “approved”;
- use official icons or release badges for an unrelated or modified product;
- offer a white-label/OEM edition using protected project branding.

Forks should use distinct names and visual identities and clearly identify their modifications. Statutory fair use and nominative use rights are not restricted.
"""
    write(ROOT / "TRADEMARK.md", trademark)

    copyright_text = f"""# Copyright and Provenance

Primary project copyright notice:

> Copyright (C) 2026 {OWNER_NAME}

The Git history remains the detailed record of authorship and changes. Third-party dependencies, generated assets, and separately attributed material remain owned by their respective copyright holders.

The repository-level audit performed during the {EFFECTIVE_DATE} license transition found {audit['commits']} commit(s), the author identities listed in `docs/LICENSE_AUDIT_{EFFECTIVE_DATE}.md`, and no external human pull-request author beyond the project owner in the GitHub history available to the audit, except automated dependency-update activity where present.

This repository audit cannot determine the effect of employment agreements, invention-assignment clauses, customer contracts, or other off-repository obligations. Those must be reviewed separately by the copyright holder before relying on commercial enforcement or granting an enterprise license.
"""
    write(ROOT / "COPYRIGHT.md", copyright_text)

    cla = f"""# Contributor License Agreement

This agreement applies to each contribution submitted to {REPO_NAME} after {EFFECTIVE_DATE}.

By submitting a contribution and affirmatively indicating agreement in the pull request, the contributor:

1. represents that they are legally entitled to submit the contribution and, when applicable, are authorized by their employer or organization;
2. retains ownership of their contribution;
3. grants Mas Ari / masarray and the project a worldwide, non-exclusive, royalty-free, perpetual, irrevocable copyright license to use, reproduce, modify, prepare derivative works of, publicly display, publicly perform, sublicense, relicense, and distribute the contribution, in source or object form, under GPL-compatible open-source terms and under separate commercial terms;
4. grants a worldwide, non-exclusive, royalty-free, perpetual, irrevocable patent license for patent claims necessarily infringed by the contribution alone or by its combination with the project as submitted; and
5. understands that the contribution is provided without warranty unless separately agreed in writing.

This is a license grant, not a transfer of copyright ownership. Contributions containing third-party material must identify that material and its license. Confidential, employer-owned, customer-owned, or unlawfully copied material must not be submitted.

Organizations that require a separately signed agreement should contact the maintainer before submitting code.
"""
    write(ROOT / "CONTRIBUTOR-LICENSE-AGREEMENT.md", cla)

    dco = """Developer Certificate of Origin
Version 1.1

Copyright (C) 2004, 2006 The Linux Foundation and its contributors.

Everyone is permitted to copy and distribute verbatim copies of this license document, but changing it is not allowed.

Developer's Certificate of Origin 1.1

By making a contribution to this project, I certify that:

(a) The contribution was created in whole or in part by me and I have the right to submit it under the open source license indicated in the file; or

(b) The contribution is based upon previous work that, to the best of my knowledge, is covered under an appropriate open source license and I have the right under that license to submit that work with modifications, whether created in whole or in part by me, under the same open source license (unless I am permitted to submit under a different license), as indicated in the file; or

(c) The contribution was provided directly to me by some other person who certified (a), (b) or (c) and I have not modified it.

(d) I understand and agree that this project and the contribution are public and that a record of the contribution (including all personal information I submit with it, including my sign-off) is maintained indefinitely and may be redistributed consistent with this project or the open source license(s) involved.
"""
    write(ROOT / "DCO.txt", dco)

    licensing = f"""# Licensing Model

## Community edition

The current public source is licensed under **GNU GPL v3.0 or later** (`GPL-3.0-or-later`). Anyone may run, inspect, modify, and redistribute it subject to the GPL. Distribution of object code must satisfy the GPL's corresponding-source requirements.

## Commercial edition and services

The copyright holder may separately offer a negotiated commercial license for proprietary integration, OEM/white-label distribution, private product branches, and contractual support. See [COMMERCIAL-LICENSE.md](../COMMERCIAL-LICENSE.md).

The GPL path remains available to companies and individuals that comply with its terms. The commercial path is for users who need different redistribution rights or business assurances.

## Historical Apache-2.0 boundary

The last revision distributed from this repository before the transition was:

`{APACHE_BASE_SHA}`

That revision and earlier public revisions remain available under Apache-2.0 on branch `archive/apache-2.0-final`. Rights already granted for those revisions are not withdrawn. The historical license text is preserved in [LICENSE-APACHE-2.0](../LICENSE-APACHE-2.0).

Changes first published after the transition are not offered under the historical Apache license unless explicitly stated.

## Contributions

New code contributions require both DCO sign-off and affirmative agreement to the [Contributor License Agreement](../CONTRIBUTOR-LICENSE-AGREEMENT.md). This preserves the public GPL edition and the separate commercial-licensing path.

## Branding

The software license does not grant official branding rights. See [TRADEMARK.md](../TRADEMARK.md).

## Important ownership boundary

Repository history can show commit and pull-request identities, but it cannot resolve employment contracts, invention assignment, or customer confidentiality obligations. Before signing a commercial license, the copyright holder should retain evidence of independent creation and obtain professional legal review where needed.
"""
    write(ROOT / "docs" / "LICENSING.md", licensing)

    authors = "\n".join(f"- `{x}`" for x in audit["authors"]) or "- None detected"
    pr_authors = "\n".join(f"- `{x}`" for x in audit["pr_authors"]) or "- No PR author data returned"
    coauthors = "\n".join(f"- `{x}`" for x in audit["coauthors"]) or "- None detected"
    packages = "\n".join(f"- {x}" for x in audit["package_refs"]) or "- No direct NuGet PackageReference entries detected"
    binaries = "\n".join(f"- `{x}`" for x in audit["binaries"]) or "- No tracked compiled/archive/capture files detected by extension"
    external = "\n".join(f"- `{x}`" for x in audit["external_pr_authors"]) or "- No external human PR authors detected; automated dependency-update accounts are not treated as human code contributors."

    audit_doc = f"""# License and Provenance Audit — {EFFECTIVE_DATE}

This is a repository-evidence audit prepared for the Apache-2.0 → GPL-3.0-or-later plus commercial-licensing transition. It is not a legal opinion.

## Scope

- Repository: `{REPO_FULL_NAME}`
- Audited base revision: `{APACHE_BASE_SHA}`
- Historical branch created: `archive/apache-2.0-final`
- Commit count visible to the audit: {audit['commits']}
- Git history fetched with full depth in GitHub Actions
- Pull-request authors queried from the GitHub API when available
- Direct NuGet references and tracked binary-like files scanned

## Git author identities

{authors}

## Pull-request author identities

{pr_authors}

### External human PR author finding

{external}

## Co-author trailers

{coauthors}

## Direct package references

{packages}

Package presence alone does not establish license compatibility. `THIRD_PARTY_NOTICES.md` must remain current, and dependency licenses must be rechecked before each commercial release.

## Tracked binary/archive/capture scan

{binaries}

## Repository conclusion

The accessible repository history supports a maintainer-controlled license transition: human pull-request activity is owned by the `masarray` account, while automated dependency updates are identifiable as automation. The historical Apache revision has been preserved before applying the new license.

## Unresolved off-repository checks

The following cannot be proven from GitHub and remain manual blockers before signing a high-value commercial agreement:

- employment agreement, invention-assignment, and moonlighting clauses;
- whether any work was created using employer time, equipment, confidential information, or customer material;
- whether sanitized SCL/PCAP samples or screenshots contain third-party rights;
- whether every third-party dependency and bundled asset is compatible with the intended distribution;
- trademark availability and registration status.

Keep independent-creation evidence, commit history, design notes, clean-room records, and copies of the relevant employment/IP agreements.
"""
    write(ROOT / "docs" / f"LICENSE_AUDIT_{EFFECTIVE_DATE}.md", audit_doc)

    pr_template = """## Summary

Describe the change and its engineering purpose.

## Validation

- [ ] Build completed
- [ ] Relevant tests completed
- [ ] No confidential SCL/PCAP/customer data included
- [ ] No proprietary or restrictive-license code copied or mechanically translated

## Contribution licensing

- [ ] I have read and agree to `CONTRIBUTOR-LICENSE-AGREEMENT.md`.
- [ ] I have the legal right and, where necessary, employer authorization to submit this contribution.
- [ ] Every commit includes a DCO sign-off (`Signed-off-by: Name <email>`).
"""
    write(ROOT / ".github" / "pull_request_template.md", pr_template)


def main() -> None:
    license_path = ROOT / "LICENSE"
    historical_path = ROOT / "LICENSE-APACHE-2.0"
    if license_path.exists() and not historical_path.exists():
        write(historical_path, read(license_path))

    gpl = download_gpl()
    write(license_path, gpl)

    audit = collect_git_audit()
    relabel_text_files()
    ensure_msbuild_license_metadata()
    patch_readme()
    patch_notice_and_third_party()
    patch_contributing()
    patch_release_scripts()
    write_legal_docs(audit)

    for path in SELF_PATHS:
        if path.exists():
            path.unlink()

    print(json.dumps({
        "repository": REPO_FULL_NAME,
        "authors": audit["authors"],
        "pr_authors": audit["pr_authors"],
        "external_pr_authors": audit["external_pr_authors"],
        "packages": audit["package_refs"],
    }, indent=2))


if __name__ == "__main__":
    main()
